namespace DraftTG.ArenaIntegration;

/// <summary>
/// The single mutable owner of one reconstructed Arena draft session.
/// Its owner must serialize calls to <see cref="Apply"/>; the engine creates no
/// threads, tasks, channels, or locks.
/// </summary>
public sealed class ArenaDraftStateEngine
{
    private static readonly ArenaDraftCoordinateComparer CoordinateComparer = new();

    private ArenaDraftSessionStatus _status = ArenaDraftSessionStatus.Idle;
    private ArenaDraftIdentifier? _draftIdentifier;
    private ArenaDraftMode? _mode;
    private string? _eventName;
    private ArenaDraftPackState? _currentPack;
    private bool _hasExplicitStart;
    private string? _entryRequestIdentifier;
    private ArenaDraftPoolState? _recoveredPool;
    private ArenaPickedCardsDiagnostic? _pickedCardsDiagnostic;
    private readonly SortedDictionary<ArenaDraftCoordinate, ArenaDraftPickRecord> _picks =
        new(CoordinateComparer);
    private readonly Dictionary<ArenaDraftCoordinate, ArenaDraftPackState> _presentedPacks = [];

    public ArenaDraftStateSnapshot Current => CreateSnapshot();

    public ArenaDraftStateUpdate Apply(ArenaDraftLogEvent draftEvent)
    {
        ArgumentNullException.ThrowIfNull(draftEvent);

        var changed = draftEvent switch
        {
            ArenaDraftLogEvent.SourceReset => ApplySourceReset(),
            ArenaDraftLogEvent.DraftStarted started => ApplyDraftStart(started.Start),
            ArenaDraftLogEvent.PackPresented presented => ApplyPack(presented.Pack),
            ArenaDraftLogEvent.PickSubmitted submitted => ApplyPick(submitted.Pick),
            ArenaDraftLogEvent.PickedCardsObserved observed => ApplyPickedCards(observed.Pool),
            ArenaDraftLogEvent.DraftCompleted completed => ApplyCompletion(completed.Completion),
            _ => throw new ArgumentOutOfRangeException(nameof(draftEvent))
        };

        return new ArenaDraftStateUpdate(CreateSnapshot(), changed);
    }

    private bool ApplySourceReset()
    {
        if (IsCompletelyIdle()) return false;
        Clear(ArenaDraftSessionStatus.Idle);
        return true;
    }

    private bool ApplyDraftStart(ArenaDraftStart start)
    {
        if (_status == ArenaDraftSessionStatus.Idle)
        {
            BeginSession(
                ArenaDraftSessionStatus.Active,
                start.DraftIdentifier,
                start.Mode,
                start.EventName,
                hasExplicitStart: true);
            _entryRequestIdentifier = start.EntryRequestIdentifier;
            return true;
        }

        if (IsClearlyDifferentStart(start) || _status == ArenaDraftSessionStatus.Completed
            && start.EntryRequestIdentifier is not null && start.EntryRequestIdentifier != _entryRequestIdentifier)
        {
            BeginSession(
                ArenaDraftSessionStatus.Active,
                start.DraftIdentifier,
                start.Mode,
                start.EventName,
                hasExplicitStart: true);
            _entryRequestIdentifier = start.EntryRequestIdentifier;
            return true;
        }

        var changed = false;
        if (_entryRequestIdentifier is null && start.EntryRequestIdentifier is not null)
        {
            _entryRequestIdentifier = start.EntryRequestIdentifier;
            changed = true;
        }
        if (_draftIdentifier is null && start.DraftIdentifier is not null)
        {
            _draftIdentifier = start.DraftIdentifier;
            changed = true;
        }
        if (_mode is null)
        {
            _mode = start.Mode;
            changed = true;
        }
        if (_eventName is null)
        {
            _eventName = start.EventName;
            changed = true;
        }
        if (!_hasExplicitStart)
        {
            _hasExplicitStart = true;
            changed = true;
        }

        // A replayed start for the same completed session enriches identity but
        // deliberately does not reopen or clear the completed session.
        return changed;
    }

    private bool ApplyPack(ArenaDraftPackPresentation presentation)
    {
        var startsNewSession = HasConflictingDraftIdentifier(presentation.DraftIdentifier);
        if (!startsNewSession && _presentedPacks.TryGetValue(presentation.Coordinate, out var existing)
            && !existing.CardIdentifiers.Equals(presentation.CardIdentifiers))
        {
            throw new ArenaDraftStateConflictException(
                ArenaDraftStateConflictKind.PackPresentation,
                presentation.Coordinate,
                existing.CardIdentifiers,
                presentation.CardIdentifiers);
        }

        var changed = PrepareForFact(presentation.DraftIdentifier, startsNewSession);
        var incoming = new ArenaDraftPackState(
            presentation.DraftIdentifier,
            presentation.Coordinate,
            presentation.CardIdentifiers);

        if (!_presentedPacks.TryGetValue(presentation.Coordinate, out existing))
        {
            _presentedPacks.Add(presentation.Coordinate, incoming);
        }
        else if (existing.DraftIdentifier is null && incoming.DraftIdentifier is not null)
        {
            _presentedPacks[presentation.Coordinate] = incoming;
        }

        // A completed coordinate or completed session cannot become actionable
        // again merely because an old pack presentation was replayed late.
        if (_picks.ContainsKey(presentation.Coordinate)
            || _status == ArenaDraftSessionStatus.Completed)
        {
            return changed;
        }

        if (_currentPack is not null
            && _currentPack.Coordinate == incoming.Coordinate
            && _currentPack.CardIdentifiers.Equals(incoming.CardIdentifiers))
        {
            if (_currentPack.DraftIdentifier is null && incoming.DraftIdentifier is not null)
            {
                _currentPack = incoming;
                return true;
            }
            return changed;
        }

        _currentPack = incoming;
        return true;
    }

    private bool ApplyPick(ArenaDraftPickSubmission submission)
    {
        var startsNewSession = HasConflictingDraftIdentifier(submission.DraftIdentifier);
        if (!startsNewSession && _picks.TryGetValue(submission.Coordinate, out var existing)
            && !existing.CardIdentifiers.Equals(submission.CardIdentifiers))
        {
            throw new ArenaDraftStateConflictException(
                ArenaDraftStateConflictKind.PickSubmission,
                submission.Coordinate,
                existing.CardIdentifiers,
                submission.CardIdentifiers);
        }

        var changed = PrepareForFact(submission.DraftIdentifier, startsNewSession);
        var incoming = new ArenaDraftPickRecord(
            submission.DraftIdentifier,
            submission.Coordinate,
            submission.CardIdentifiers);

        if (_picks.TryGetValue(submission.Coordinate, out existing))
        {
            if (existing.DraftIdentifier is null && incoming.DraftIdentifier is not null)
            {
                _picks[submission.Coordinate] = incoming;
                changed = true;
            }
        }
        else
        {
            _picks.Add(submission.Coordinate, incoming);
            changed = true;
        }

        if (_currentPack?.Coordinate == submission.Coordinate)
        {
            _currentPack = null;
            changed = true;
        }

        if (_recoveredPool is { } pool)
        {
            var covered = new ArenaCardMultiset(_picks.Values.Where(p => pool.CurrentCoordinate is null
                || CoordinateComparer.Compare(p.Coordinate, pool.CurrentCoordinate) < 0).SelectMany(p => p.CardIdentifiers));
            if (!pool.Cards.Contains(covered))
                changed |= RejectPool(ArenaPickedCardsDiagnosticKind.ExactHistoryMismatch,
                    "Known completed selections contradict the recovered PickedCards snapshot; exact history retained.");
        }

        return changed;
    }

    private bool ApplyCompletion(ArenaDraftCompletion completion)
    {
        // A late deck selection response or course list cannot complete a different currently observed session.
        if (completion.Origin is ArenaDraftCompletionOrigin.DeckSelection or ArenaDraftCompletionOrigin.CourseSnapshot
            && (HasConflictingDraftIdentifier(completion.DraftIdentifier)
                || _eventName is not null && !string.Equals(_eventName, completion.EventName, StringComparison.Ordinal))) return false;
        // A course list is only usable as a full drafted pool; otherwise it is not draft evidence at all.
        if (completion.Origin == ArenaDraftCompletionOrigin.CourseSnapshot
            && completion.CourseCardPool?.Count != ArenaQuickDraftCoordinates.PackCount * ArenaQuickDraftCoordinates.PicksPerPack) return false;
        var startsNewSession = HasConflictingDraftIdentifier(completion.DraftIdentifier);
        var changed = false;

        if (_status == ArenaDraftSessionStatus.Idle || startsNewSession)
        {
            BeginSession(
                ArenaDraftSessionStatus.Completed,
                completion.DraftIdentifier,
                completion.Mode,
                completion.EventName,
                hasExplicitStart: false);
            changed = true;
        }

        if (_draftIdentifier is null && completion.DraftIdentifier is not null)
        {
            _draftIdentifier = completion.DraftIdentifier;
            changed = true;
        }
        if (_eventName is null && completion.EventName is not null)
        {
            _eventName = completion.EventName;
            changed = true;
        }
        if (_mode is null && completion.Mode is not null)
        {
            _mode = completion.Mode;
            changed = true;
        }
        if (_status != ArenaDraftSessionStatus.Completed)
        {
            _status = ArenaDraftSessionStatus.Completed;
            changed = true;
        }
        if (_currentPack is not null)
        {
            _currentPack = null;
            changed = true;
        }
        if (completion.Origin == ArenaDraftCompletionOrigin.DeckSelection && completion.FinalPickedCards is { } cards)
            changed |= ApplyFinalPickedCards(cards, requireQuick: true, "DeckSelect PickedCards");
        if (completion.Origin == ArenaDraftCompletionOrigin.CourseSnapshot && completion.CourseCardPool is { } pool)
            changed |= ApplyFinalPickedCards(pool, requireQuick: false, "Course CardPool");
        return changed;
    }

    private bool ApplyFinalPickedCards(ArenaCardIdentifierList identifiers, bool requireQuick, string source)
    {
        var incoming = new ArenaCardMultiset(identifiers);
        var known = Current.DraftedPool;
        var expected = ArenaQuickDraftCoordinates.PackCount * ArenaQuickDraftCoordinates.PicksPerPack;
        if ((requireQuick && _mode?.Kind != ArenaDraftModeKind.Quick) || incoming.Count != expected || !incoming.Contains(known))
            return RejectPool(ArenaPickedCardsDiagnosticKind.FinalPoolMismatch,
                $"{source} mismatch: recovered/exact pool {known.Count}, completion pool {incoming.Count}, expected {expected}. "
                + "Completion inventory does not agree with known drafted multiplicities; recovered/exact pool retained, not merged.");

        // Prefer an already complete recovered/exact multiset. An agreeing response only confirms it.
        var changed = _pickedCardsDiagnostic is not null;
        _pickedCardsDiagnostic = null;
        if (known.Count == expected) return changed;
        // A full successful Quick Draft response can fill missing picks, but never removes known copies
        // or assigns invented coordinates to previously unobserved selections.
        _recoveredPool = new(null, incoming);
        return true;
    }

    private bool ApplyPickedCards(ArenaPickedCardsSnapshot snapshot)
    {
        var changed = PrepareForFact(snapshot.DraftIdentifier, HasConflictingDraftIdentifier(snapshot.DraftIdentifier));
        var coordinate = snapshot.CurrentCoordinate;
        var cards = snapshot.Cards;
        if (coordinate is not null && (!ArenaQuickDraftCoordinates.IsSupported(coordinate)
            || cards.Count != ArenaQuickDraftCoordinates.CompletedPicksBefore(coordinate)))
            return RejectPool(ArenaPickedCardsDiagnosticKind.UnexpectedCardCount,
                "PickedCards count does not agree with the current Quick Draft coordinate; pool snapshot ignored.") || changed;

        if (_recoveredPool?.CurrentCoordinate is { } previous && coordinate is not null
            && CoordinateComparer.Compare(coordinate, previous) < 0)
            return RejectPool(ArenaPickedCardsDiagnosticKind.BackwardCoordinate,
                "PickedCards coordinate moved backwards without a session/source reset; pool snapshot ignored.") || changed;
        if (_recoveredPool is { } baseline && !cards.Contains(baseline.Cards))
            return RejectPool(ArenaPickedCardsDiagnosticKind.RemovedCards,
                "PickedCards removed drafted occurrences without a session/source reset; pool snapshot ignored.") || changed;

        var coveredExactPicks = new ArenaCardMultiset(_picks.Values.Where(p => coordinate is null
                || CoordinateComparer.Compare(p.Coordinate, coordinate) < 0).SelectMany(p => p.CardIdentifiers));
        if (!cards.Contains(coveredExactPicks))
            return RejectPool(ArenaPickedCardsDiagnosticKind.ExactHistoryMismatch,
                "PickedCards does not contain the known completed selections; pool snapshot ignored.") || changed;

        // Only a consecutive coordinate and one added occurrence prove an exact selection.
        // Explicit and inferred selections use the very same conflict/idempotence path.
        if (_recoveredPool?.CurrentCoordinate is { } prior && coordinate is not null
            && ArenaQuickDraftCoordinates.IsNext(prior, coordinate)
            && cards.TryGetSingleAddition(_recoveredPool.Cards, out var added))
            changed |= ApplyPick(new(snapshot.DraftIdentifier, prior, new([added!])));

        var incoming = new ArenaDraftPoolState(coordinate, cards);
        changed |= incoming != _recoveredPool || _pickedCardsDiagnostic is not null;
        _recoveredPool = incoming;
        _pickedCardsDiagnostic = null;
        return changed;
    }

    private bool RejectPool(ArenaPickedCardsDiagnosticKind kind, string message)
    {
        var diagnostic = new ArenaPickedCardsDiagnostic(kind, message);
        var changed = diagnostic != _pickedCardsDiagnostic;
        _pickedCardsDiagnostic = diagnostic;
        return changed;
    }

    private bool PrepareForFact(
        ArenaDraftIdentifier? incomingIdentifier,
        bool startsNewSession)
    {
        if (_status == ArenaDraftSessionStatus.Idle || startsNewSession)
        {
            BeginSession(
                ArenaDraftSessionStatus.Active,
                incomingIdentifier,
                mode: null,
                eventName: null,
                hasExplicitStart: false);
            return true;
        }

        if (_draftIdentifier is null && incomingIdentifier is not null)
        {
            _draftIdentifier = incomingIdentifier;
            return true;
        }
        return false;
    }

    private bool IsClearlyDifferentStart(ArenaDraftStart start)
    {
        if (HasConflictingDraftIdentifier(start.DraftIdentifier)) return true;
        if (!_hasExplicitStart) return false;

        // When either side lacks a reliable draft ID, a different explicit raw
        // event identity is the conservative secondary new-session signal.
        if (_draftIdentifier is null || start.DraftIdentifier is null)
        {
            return !string.Equals(_eventName, start.EventName, StringComparison.Ordinal)
                || _mode != start.Mode;
        }
        return false;
    }

    private bool HasConflictingDraftIdentifier(ArenaDraftIdentifier? incomingIdentifier) =>
        _draftIdentifier is not null
        && incomingIdentifier is not null
        && _draftIdentifier != incomingIdentifier;

    private void BeginSession(
        ArenaDraftSessionStatus status,
        ArenaDraftIdentifier? draftIdentifier,
        ArenaDraftMode? mode,
        string? eventName,
        bool hasExplicitStart)
    {
        Clear(status);
        _draftIdentifier = draftIdentifier;
        _mode = mode;
        _eventName = eventName;
        _hasExplicitStart = hasExplicitStart;
    }

    private void Clear(ArenaDraftSessionStatus status)
    {
        _status = status;
        _draftIdentifier = null;
        _mode = null;
        _eventName = null;
        _currentPack = null;
        _hasExplicitStart = false;
        _entryRequestIdentifier = null;
        _recoveredPool = null;
        _pickedCardsDiagnostic = null;
        _picks.Clear();
        _presentedPacks.Clear();
    }

    private bool IsCompletelyIdle() =>
        _status == ArenaDraftSessionStatus.Idle
        && _draftIdentifier is null
        && _mode is null
        && _eventName is null
        && _currentPack is null
        && _picks.Count == 0
        && _presentedPacks.Count == 0;

    private ArenaDraftStateSnapshot CreateSnapshot() =>
        new(
            _status,
            _draftIdentifier,
            _mode,
            _eventName,
            _currentPack,
            new ArenaDraftPickRecordList(_picks.Values))
        { RecoveredPool = _recoveredPool, PickedCardsDiagnostic = _pickedCardsDiagnostic };

    private sealed class ArenaDraftCoordinateComparer : IComparer<ArenaDraftCoordinate>
    {
        public int Compare(ArenaDraftCoordinate? left, ArenaDraftCoordinate? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var packComparison = left.Pack.CompareTo(right.Pack);
            return packComparison != 0 ? packComparison : left.Pick.CompareTo(right.Pick);
        }
    }
}
