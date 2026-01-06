# 自動検品ツール（KENPINTOOL）

## ドキュメント

- [ドキュメント目次](doc/README.md)
- [企画書](doc/01_企画/企画書.md)
- [要件定義書（統合）](doc/02_要件/要件定義書.md)
- [業務要件定義書](doc/02_要件/業務要件定義書.md)
- [機能要件定義書](doc/02_要件/機能要件定義書.md)
- [非機能要件定義書](doc/02_要件/非機能要件定義書.md)
- [NG定義・判断基準マスタの具体化](doc/05_統制/NG定義・判断基準マスタの具体化.md)
- [デザイン要件定義書](doc/03_UI/デザイン要件定義書.md)
- [UI詳細設計書](doc/03_UI/UI詳細設計書.md)
- [技術選定](doc/04_技術/技術選定.md)
- [プロトタイプ計画](doc/04_技術/プロトタイプ計画.md)

## プロトタイプ（WPF）

プロジェクト: `src/KenpinTool.Prototype`

### 実行

```powershell
python tools/generate_sample_images.py samples/inputs
dotnet run --project src/KenpinTool.Prototype -- ".\\samples\\inputs"
```

### 入力

- JPEG/PNG/BMP/TIFF のフォルダを指定します。
- ファイル名に `QLT-05` / `STR-01S` などのNGコード文字列を含めると、ダミー検知としてUIに反映されます。
  - 例: `006_QLT-05.bmp` / `002_STR-01S.bmp`

### 出力（ログ/CSV）

- `case.json` / `audit.jsonl` / `result.csv` を `"%LOCALAPPDATA%\KenpinTool.Prototype\runs\..."` に出力します。

### ショートカット（プロトタイプ）

- `J` / `→`: 次ページ
- `K` / `←`: 前ページ
- `N`: 次のNG/疑いへジャンプ
- `Space`: OK
- `S`: 再スキャン
- `E`: 例外承認（NG-B/NG-Cのみ）
- `C`: 比較モード
- `F`: フィルタ（NG/疑いのみ）
- `Z`: ズーム切替（1x/2x）
### 起動と操作

- 起動コマンド: `dotnet run --project src/KenpinTool.Prototype`
- 画像ビューア: `Z` でフィット/等倍を切り替え、Ctrl+ホイールまたは Ctrl+`+`/`-` で自由に拡大縮小できます。
- 判定操作: `Space` で OK（解析完了後）、`S` で再スキャン、`E` で例外承認。解析が未完了のページではこれらのボタンは非活性になります。
- レポート/CSV: 処理中の案件情報は `runs/...` 配下に出力され、CSV・Auditログ・PDFレポートから追跡できます。
