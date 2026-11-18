# Icon 33 thế Huyền Nguyệt — 1.1.11

Đã thêm 33 PNG riêng cho 33 recipe trong `Forge/huyen_nguyet.json`, từ Novice đến God. Không thay icon của trường phái hoặc ba kỹ thuật native hiện có.

Nguồn vẽ bằng code nằm trong PixelStudio độc lập (`src/pixelstudio/recipes/huyen_nguyet.py`); tên, cấp, mô tả và filename được đối chiếu Forge. Thiết kế dùng nền tối/viền kim loại gọn như icon Võ Thần, màu nguyệt bạc–lam, kem vàng, tím. Các motif phân biệt chưởng, nguyệt nhận, thân pháp, kính/kết giới, dư ảnh, trận và tuyệt kỹ. Không sinh ảnh hoặc texture mỗi khung hình trong game.

Asset mới ở `GameResources/ui/Icons/sakuga/iconHn*.png`, PNG 32×32 RGBA nền trong suốt. Tên PascalCase đúng cơ chế `Technique.Pascal(id)` của Forge: khi khởi động, engine tự chọn icon riêng thay cho fallback trường phái. Các cấp I–III của một thế vẫn dùng cùng icon như cơ chế hiện tại.

Không đổi ID trait, công thức chiêu, AI, sát thương, cooldown, điều kiện thi triển, VFX hoặc save. Cần khởi động lại game/mod để Forge dựng lại danh mục.

Kiểm tra offline: 33 ID khớp, đủ file, PNG32 RGBA, 33 ảnh khác nhau tại bộ xuất, parser thực chọn đúng đường dẫn từng thế và bộ kiểm tra Võ Thần. Chưa kiểm tra trực tiếp bảng trait trong game.

Giữ tác giả mod gốc Turbo; 33 icon này là phần bổ sung của nhánh hiện tại, vẽ bằng công cụ riêng, không tuyên bố là artwork gốc của Turbo.
