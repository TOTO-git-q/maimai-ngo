# Round 3 candidate — awaiting in-game review

The source and DLL in this folder implement the cross-phase candidate but have not been visually tested in this revision. The prior `resume4_candidate` was captured in `../mytest/`; it is not evidence for the new placements.

- Jacket: `Null_SelectorBackgroundTarget/UI_MSS_GenreSelect(Clone)/Null_UI_MSS_GenreSelect/Scrolling_Jacket` is hidden in every eligible phase, including Genre. The rest of the genre artwork can still be shown.
- Marquee: `Null_SelectorBackgroundTarget/UI_CMN_SelectorBG(Clone)/Null_CanvasGroup/Text` and `Null_SelectorBackgroundTarget/UI_MSS_GenreSelect(Clone)/Null_UI_MSS_GenreSelect/UI_SelectorBG/Null_CanvasGroup/Text` are hidden. These are visual CanvasGroup changes, restored when the layout is disabled.
- National data: `Null_UI_MSS_RivalData/UI_MSS_NationalData(Clone)` is hidden; the separate rival panel and game-owned activation logic remain untouched.
- Difficulty: restore the original `UI_CMN_TitleBase/Null_UI_CMN_TitleBase` Title/Base/Base_Shadow art, suppress the small replacement label, and place the `UI_CMN_TitleBase` root at Main-local (0,100), 0.55 of captured XY scale. It is restored on phase exit/disable. The coordinates are provisional.
- Option: `UI_ChainLists/NormalChainList` is scaled to 0.62 of its baseline and offset -45 local X while in Option, then restored. The coordinates are provisional and require screenshots at multiple carousel positions.
- Partner stays visible across eligible phases; reactions only run in Music. The existing selectView switch still disables the feature. No preset files were edited.

Build: prescribed build.ps1, no -Install, zero warnings/errors. Headless: 12 phase-policy checks, existing tests and all eight models passed. These do not render Unity UI. DLL SHA-256: 61BD5A06677BCCE8251833AC5F6DDA5654AB4F6F5E84B58DBBDB133C3CE39208.

Inspect Genre/Music/Difficulty/Menu/Option and Sort if reachable, check the hidden elements and Difficulty/Option geometry and transitions, then capture a real Result-to-select burst. The prior first-entry burst is suggestive but its start time does not prove the first rendered frame. Check special modes, restore/disable and two-player behavior as available. Do not regard the earlier `compare_phases.png` as this revision's final comparison.
