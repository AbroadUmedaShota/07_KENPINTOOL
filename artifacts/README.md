# artifacts

ローカル実行・検証で生成される成果物（配布物、ログ、出力ファイル等）を集約するためのフォルダです。

- 原則として `artifacts/` 配下の生成物は Git にコミットしません（`.gitignore` で除外）。
- プロトタイプの実行ログは既定で `"%LOCALAPPDATA%\\KenpinTool.Prototype\\runs\\..."` に出力されます。

