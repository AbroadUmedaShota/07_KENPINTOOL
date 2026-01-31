# サンプルデータ

`samples/inputs/` はプロトタイプ動作確認用の画像入力フォルダです（Git管理対象外）。

## 生成（サンプル画像）

```powershell
python tools/generate_sample_images.py samples/inputs
```

## 実行例（プロトタイプ）

```powershell
dotnet run --project src/KenpinTool.Prototype -- ".\\samples\\inputs"
```

