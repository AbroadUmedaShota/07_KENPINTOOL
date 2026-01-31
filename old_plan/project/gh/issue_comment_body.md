### Implementation Proposal

To resolve Issue #14, I will refine the implementation as follows.

#### 1. Domain Logic Guard (NG-A)
- **File**: `src/KenpinTool.Prototype/Models/PageItem.cs`
- **Change**: Add a validation check in `ApplyException`. If `HasFatalActiveDetections` is true, throw an `InvalidOperationException`. This ensures the business rule is enforced at the model level, regardless of UI state.

#### 2. Focus Management
- **File**: `src/KenpinTool.Prototype/Views/MainWindow.xaml`
- **Change**: Set `Focusable="False"` on the action buttons (OK, Rescan, Exception, Toggle checks).
    - **Reason**: In this high-speed inspection tool, the primary interaction is via J/K keys. Buttons are secondary/mouse-only fallback. Preventing them from stealing focus ensures J/K keys remain active on the Window/ListView.
    - *Note*: TextBoxes (like Folder Path) will remain focusable.

#### 3. Async Image Loading Safety
- **File**: `src/KenpinTool.Prototype/ViewModels/MainViewModel.cs`
- **Change**: In `RefreshImagesAsync`, add a post-await check for `cts.Token.IsCancellationRequested`.
    - **Reason**: Although `ImageLoaderService` handles cancellation, a race condition could theoretically occur if the task completes just as a cancellation is signaled. Explicitly checking the token before updating `CurrentImage` guarantees we only display valid results for the current page selection.

#### 4. Definition of Done
- [ ] `PageItem` throws exception on invalid NG-A exception attempt.
- [ ] Clicking buttons does not break J/K navigation.
- [ ] Image loading is robust against rapid switching.

---
If you approve, please reply with "Approve".