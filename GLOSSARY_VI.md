# Thuật ngữ Việt hóa — Võ Đạo Chiến Thần

## Cảnh giới

| Trung/Anh | Việt chuẩn |
|---|---|
| 不入流 / Combat Novice | Bất Nhập Lưu |
| 后天 / Combat Adept | Hậu Thiên |
| 先天 / Combat Expert | Tiên Thiên |
| 宗师 / Combat Master | Tông Sư |
| 大宗师 / Grandmaster | Đại Tông Sư |
| 天人 / Combat God | Thiên Nhân |
| 陆地神仙 / Primordial | Lục Địa Thần Tiên |

## Hệ thống chiến đấu

| Trung/Anh | Việt chuẩn |
|---|---|
| 战技 / Technique | Võ Kỹ |
| 神技 / God Ability | Thần Kỹ |
| 合体战技 / Team Technique | Hợp Kích Võ Kỹ |
| 气力 / Mana | Chân Khí |
| 气场 / Aura | Khí Tràng |
| 战意气焰 / Battle Flare | Chiến Diễm |
| 压迫 / Pressure Aura | Uy Áp |
| 功法流派 / Combat School | Võ Phái |
| 突破 / Promotion | Đột Phá |
| 证道 / Ascension | Chứng Đạo |
| 世界末日 / World's End | Tận Thế |

## Quy tắc kỹ thuật

- Chỉ sửa value hiển thị; không đổi key `sakuga_*`, ID, GUID, callback hoặc tên asset.
- Giữ nguyên placeholder như `{0}`, `{1}`, `$a$`, `$b$` và escape `\n`.
- Tên chiêu ưu tiên Hán–Việt; mô tả dùng tiếng Việt tự nhiên và nói rõ cơ chế.
- Bản Việt được ghi vào `Locales/**/en.json`; giữ `cz.json` làm nguồn Hán hóa đối chiếu.
- JSON dùng UTF-8 không BOM và phải parse thành công sau mỗi batch.
