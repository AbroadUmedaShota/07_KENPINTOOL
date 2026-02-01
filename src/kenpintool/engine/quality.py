import cv2
import numpy as np
from typing import List, Tuple

class QualityEngine:
    """画像の品質（色線、角折れ等）を解析するエンジン。"""

    def __init__(self, color_streak_threshold: float = 30.0):
        self.color_streak_threshold = color_streak_threshold

    def detect_color_streaks(self, image_path: str) -> List[dict]:
        """
        垂直方向の色線（スジ）を検知する。
        
        Args:
            image_path: 画像ファイルのパス
            
        Returns:
            検知した情報のリスト
        """
        img = cv2.imread(image_path)
        if img is None:
            return []

        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        h, w = gray.shape

        # 垂直方向に平均値を計算 (各列の平均)
        col_means = np.mean(gray, axis=0)
        
        # 隣接する列との差分をとることで、急激な変化（スジ）を見つける
        diff = np.abs(np.diff(col_means))
        
        # 閾値を超える箇所を抽出
        streak_indices = np.where(diff > self.color_streak_threshold)[0]
        
        results = []
        for idx in streak_indices:
            # 矩形領域として定義 (x, y, w, h)
            # 全高にわたるスジとして定義
            results.append({
                "code": "QLT-05",
                "name": "色線・スジ",
                "x": float(idx) / w,
                "y": 0.0,
                "width": 2.0 / w,
                "height": 1.0,
                "confidence": float(min(diff[idx] / 100.0, 1.0))
            })
            
        return results

    def detect_folded_corners(self, image_path: str) -> List[dict]:
        """
        角折れを検知する（プロトタイプ版）。
        書類の4隅付近に背景色（黒）の領域があるかを確認する。
        
        Args:
            image_path: 画像ファイルのパス
            
        Returns:
            検知した情報のリスト
        """
        img = cv2.imread(image_path)
        if img is None:
            return []

        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        h, w = gray.shape
        
        # 二値化 (黒い領域を抽出)
        _, thresh = cv2.threshold(gray, 30, 255, cv2.THRESH_BINARY_INV)
        
        # 四隅のチェック領域サイズ (画像の5%程度)
        check_size_w = int(w * 0.15)
        check_size_h = int(h * 0.15)
        
        corners = [
            ("top_left", (0, 0, check_size_w, check_size_h)),
            ("top_right", (w - check_size_w, 0, check_size_w, check_size_h)),
            ("bottom_left", (0, h - check_size_h, check_size_w, check_size_h)),
            ("bottom_right", (w - check_size_w, h - check_size_h, check_size_w, check_size_h))
        ]
        
        results = []
        for name, (x, y, cw, ch) in corners:
            roi = thresh[y:y+ch, x:x+cw]
            # 黒い領域（反転後なので白）の面積を計算
            black_area = cv2.countNonZero(roi)
            total_area = cw * ch
            
            # 領域の1%以上が黒（背景）であれば角折れと疑う
            if black_area > total_area * 0.01:
                results.append({
                    "code": "QLT-01",
                    "name": f"角折れ疑い ({name})",
                    "x": float(x) / w,
                    "y": float(y) / h,
                    "width": float(cw) / w,
                    "height": float(ch) / h,
                    "confidence": float(min(black_area / (total_area * 0.1), 1.0))
                })
                
        return results

if __name__ == "__main__":
    # 簡易テスト用
    engine = QualityEngine()
    # results = engine.detect_color_streaks("sample.jpg")
    # print(results)
