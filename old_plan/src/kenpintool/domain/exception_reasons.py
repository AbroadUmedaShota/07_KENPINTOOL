from __future__ import annotations

# 例外承認理由は固定コードのみ許可（自由記述で代替不可）
EXCEPTION_REASONS = [
    ("EXC-01", "再取得不可"),
    ("EXC-02", "依頼元承認済"),
    ("EXC-03", "仕様上許容"),
]
