# 自動検品ツール（KENPINTOOL）

## ドキュメント

- `doc/企画書.md`
- `doc/要件定義書.md`
- `doc/業務要件定義書.md`
- `doc/機能要件定義書.md`
- `doc/非機能要件定義書.md`
- `doc/NG定義・判断基準マスタの具体化.md`
- `doc/デザイン要件定義書.md`
- `doc/UI詳細設計書.md`
- `doc/技術選定.md`
- `doc/プロトタイプ計画.md`

## プロトタイプ（WPF）

プロジェクト: `src/KenpinTool.Prototype`

### 実行

```powershell
python tools/generate_sample_images.py sample-data
dotnet run --project src/KenpinTool.Prototype -- ".\\sample-data"
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
