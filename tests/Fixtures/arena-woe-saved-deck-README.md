# Real WOE saved-deck semantic fixture

`arena-woe-saved-deck-live.jsonl` contains privacy-safe subsets of observed records from the October 4, 2026 Player.log: the Draft → DeckBuilder transition, the direct server `CourseDeck` snapshot after `EventSetDeckV3`, and DeckBuilder → EventLanding. In the pre-restart audit these were lines 551, 563 and 564. The structural audit was saved before Arena restarted and rotated Player.log.

Only public event names, main/sideboard card IDs/counts and scene names remain. Account/request/course/deck identifiers, summaries, cosmetics, names and unrelated courses were removed. The scene records retain only their observed from/to fields. The saved response retains only its observed event identity and main/sideboard data. No current-editor snapshot is invented.

The observed deck contains 40 cards: 23 drafted spells, one drafted nonbasic (Evolving Wilds), eight Mountains and eight Forests. Eighteen drafted copies are in the sideboard. These nonbasic main/sideboard counts conserve the completed 42-card WOE pool.

`arena-woe-saved-deck-cards.json` reuses the existing public WOE completion catalog subset and adds the exact local Scryfall metadata for Arena stock basics 75900 (Mountain) and 75903 (Forest). It contains no screenshot or personal data.

This fixture certifies **historical saved counts**, not unsaved editor state or live edit notifications. Tests explicitly prevent saved snapshots from authorizing automatic add/remove guidance.
