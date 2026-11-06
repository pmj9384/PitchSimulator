# 잔디 텍스처 출처

잔디 마모 셰이더(`Assets/Shaders/GrassWear.shader`)가 쓰는 알베도 2장. 둘 다 ambientCG의 CC0(퍼블릭 도메인) 재질이다. 2026-11-06에 각 재질 페이지에서 "Creative Commons CC0 license" 문구를 확인하고 1K-JPG의 Color 맵만 받았다.

| 파일 | 출처 | 용도 |
| --- | --- | --- |
| `Grass001_Color_1K.jpg` | https://ambientcg.com/a/Grass001 (Grass 001, CC0 1.0) | 좋은 잔디 A |
| `Ground037_Color_1K.jpg` | https://ambientcg.com/a/Ground037 (Ground 037, CC0 1.0) | 마른 잔디·맨땅 B(셰이더에서 누런 쪽으로 틴트) |

라이선스 원문: https://creativecommons.org/publicdomain/zero/1.0/

`GrassWearMask.png`는 외부 자산이 아니다. `Tools/Pitch/잔디 마모 에셋 만들기`(`GrassWearBuilder`)가 `FieldBounds` 상수로 생성한다.
