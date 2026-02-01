import sys
import os

# プロジェクトルートをパスに追加
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from kenpintool.ui.main_window import main

if __name__ == "__main__":
    main()
