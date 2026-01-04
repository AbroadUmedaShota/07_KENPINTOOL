### Self-Review Report

I have conducted a self-review and confirmed that the implementation aligns with the project's standards and the requirements of Issue #8.

**Changes Verification:**
- [x] **NuGet Packages**: Added CommunityToolkit.Mvvm, Hosting, and OpenCvSharp4.
- [x] **Architecture**: Implemented Generic Host in `App.xaml.cs` for DI.
- [x] **Image Pipeline**: Created `ImageLoaderService` using `System.Threading.Channels` and OpenCV for non-blocking loading.
- [x] **UI**: Recreated `MainWindow` in `Views/` with a 3-pane layout (List, Image, Controls).
- [x] **Refactoring**: Updated `MainViewModel` to use `CommunityToolkit.Mvvm` and injected services.
- [x] **Cleanup**: Removed obsolete `Infrastructure` classes and incorrectly placed files.

**Quality Gate Assessment:**
- **Computational Complexity**: The image loading pipeline uses a bounded channel (size 5) and `DropOldest` strategy to ensure memory usage remains stable even if the user scrolls rapidly.
- **Security**: No sensitive info involved.
- **Scalability**: The architecture decouples UI from image loading, suitable for future extensions.

**Design Trade-offs:**
- **BitmapSource vs WriteableBitmap**: Used `Mat.ToBitmapSource()` with `Freeze()` for simplicity and thread safety. `WriteableBitmap` might be needed later for drawing overlays pixel-by-pixel, but for now, overlays are handled via WPF shapes (implied by `OverlayRect` usage in ViewModel, though not fully visualized in XAML yet aside from simple usage).

---
Please review and approve the merge.
