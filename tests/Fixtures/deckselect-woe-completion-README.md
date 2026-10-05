# Live WOE draft completion fixture

Derived from the current Windows Player.log on 2026-10-04, original lines 542, 543, 545, 546 and 551. The records preserve raw framing and nested JSON structure: final P3P14 status with 41 PickedCards, outbound final selection 87053, a standalone incoming response marker, DeckSelect/Completed with 42 PickedCards, and the Draft → DeckBuilder scene change.

The correlation UUID is replaced with `redacted-correlation`. DTO_InventoryInfo (currencies, inventory, cosmetics) is omitted. No account identifiers, user identifiers, decks, course IDs or private log history are included. Public event name and public card IDs/multiplicities remain unchanged. Card order is not interpreted as pick chronology. The final multiset equals the prior 41 plus one occurrence of 87053.

`deckselect-woe-cards.json` contains the 38 public Scryfall printing records needed to resolve these 42 drafted copies, restricted to identity and gameplay metadata. It is a catalog fixture rather than an Arena deck selection. Production parser/state/coordinator tests consume the raw sequence directly; no manual Completed state or builder invocation is used for activation tests.
