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
            return true;
        }

        if (IsClearlyDifferentStart(start))
        {
            BeginSession(
                ArenaDraftSessionStatus.Active,
                start.DraftIdentifier,
                start.Mode,
                start.EventName,
                hasExplicitStart: true);
            return true;
        }

        var changed = false;
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

        return changed;
    }

    private bool ApplyCompletion(ArenaDraftCompletion completion)
    {
        var startsNewSession = HasConflictingDraftIdentifier(completion.DraftIdentifier);
        var changed = false;

        if (_status == ArenaDraftSessionStatus.Idle || startsNewSession)
        {
            BeginSession(
                ArenaDraftSessionStatus.Completed,
                completion.DraftIdentifier,
                mode: null,
                completion.EventName,
                hasExplicitStart: false);
            return true;
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
            new ArenaDraftPickRecordList(_picks.Values));

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
