import cv2
import numpy as np
import os

def generate_samples(output_dir: str):
    if not os.path.exists(output_dir):
        os.makedirs(output_dir)

    # 1. 正常な画像 (白紙)
    normal = np.full((1000, 700, 3), 255, dtype=np.uint8)
    cv2.imwrite(os.path.join(output_dir, "normal_001.jpg"), normal)

    # 2. 色線入りの画像
    streak = normal.copy()
    # 垂直な赤い線を入れる (x=350)
    cv2.line(streak, (350, 0), (350, 1000), (0, 0, 255), 2)
    cv2.imwrite(os.path.join(output_dir, "streak_002.jpg"), streak)

    # 3. 角折れ入りの画像 (右上を黒く三角形に塗りつぶす)
    folded = normal.copy()
    pts = np.array([[700, 0], [600, 0], [700, 100]], np.int32)
    cv2.fillPoly(folded, [pts], (0, 0, 0))
    cv2.imwrite(os.path.join(output_dir, "folded_003.jpg"), folded)

    print(f"Sample images generated in {output_dir}")

if __name__ == "__main__":
    generate_samples("samples/inputs")
