# Huyền Nguyệt VFX — 1.1.12

Đợt 1: bộ nguyên liệu gốc từ PixelStudio, không thay thế tác giả/cơ chế gốc Turbo.

Nguồn sinh: PixelStudio/src/pixelstudio/lunar_vfx.py. Bốn PNG 49×49 và bản xuất
LunarPixelBank.cs.txt nằm trong PixelStudio/exports/vfx/huyen_nguyet.
Code/Choreo/LunarPixelBank.cs là bản xuất tương ứng; không chỉnh tay pixel ở đây.
MoonArt cache bốn sprite, SchoolStyle dùng pool Fx sẵn có.

- Tụ lực: nguyệt thể riêng, không còn chỉ thu nhỏ sprite đạn.
- Va chạm: lõi sáng nở nhanh, ba mảnh trăng tan, vòng xung kích cũ.
- Bộ pháp: cung dư ảnh tối đa một lần/0.09 giây/move; timer riêng với ghost gốc.
- Nguyệt Kính Phản Kích: hiện gương trong 0.25 giây đầu thế, chỉ trang trí.

Hook chung tác động nhiều thế, nhưng chưa biên đạo lại toàn bộ 33 thế/God.
Không đổi target, damage, cooldown, AI, lịch combo hoặc thêm quét world.
Hiệu ứng mới có kiểm tra trong camera, thời gian sống hữu hạn, không giữ particle.

Thử trong game: Nguyệt Quang Chưởng, Tiểu Nguyệt Nhận, Nguyệt Ảnh Bộ,
Nguyệt Kính Phản Kích; xem gần/xa và nhiều cặp chiến đấu. Build offline không
chứng minh FPS hoặc cảm giác lực trong game. Đợt tiếp theo điều chỉnh theo phản hồi.
