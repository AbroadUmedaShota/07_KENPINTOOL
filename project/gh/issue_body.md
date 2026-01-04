### 1. Pre-investigation Summary
Review feedback from PoC-0 implementation (Issue #8) highlighted potential risks in UI reliability and domain logic integrity.
- **Focus Loss**: Keyboard shortcuts may stop working after UI interactions.
- **NG-A Logic**: The rule "No Exception for NG-A" relies solely on UI state.
- **Race Conditions**: Rapid navigation might cause image/metadata mismatch.

### 2. Proposal
Address the feedback by improving the codebase:
1.  **Focus Management**: Implement `FocusManager` or enforce input bindings at the Window level to prevent focus trapping.
2.  **Domain Guard**: Enforce "No Exception for NG-A" rule within `PageItem` or `PageDecision` models.
3.  **Async Safety**: Verify and strengthen `ImageLoaderService` usage with `CancellationToken` checks to ensure eventual consistency.

### 3. Definition of Done
- [ ] Keyboard shortcuts (J/K) function correctly after clicking buttons or lists.
- [ ] `PageItem.ApplyException` throws or rejects if detections include NG-A.
- [ ] Rapid page navigation stabilizes to the correct image/metadata pair.