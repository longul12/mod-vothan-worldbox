# Tiến độ nhánh Võ Thần

Mốc đầu tiên đưa lên Git: **1.1.0**.

## 1.1.3 — Huyền Nguyệt, bước 3

Thêm 6 thế Expert, tổng 18 thế. Có chưởng lực tầm xa, nguyệt nhận hình quạt, hất tung mở combo, phân ảnh chia tổng sát thương, nguyệt cầu nổ theo đợt và guard phản công xung kích. Không thêm NPC phân thân thật; nguyệt cầu đặt crater=0. Kết giới hiện chỉ là phòng thủ ngắn. Kiểm tra biên dịch, parser Forge, ID, giới hạn chi phí và công thức đã đạt; chưa thử game. Tiếp theo là Master và chuyển backend Nguyệt Khuyết từ CWP.

## 1.1.2 — Huyền Nguyệt, bước 2

Thêm 6 thế Adept, nâng tổng số lên 12: liên hoàn chưởng, nguyệt nhận liên tiếp/quay về, lướt truy kích, quét hình quạt và đỡ phản công. Giữ AI/cấp I–III/hồi chiêu của engine. Nguyệt nhận Boomerang tính ngân sách hai chiều bay, không tăng sát thương mỗi lượt quá mức. Tạm trao phái thủ công; chưa chuyển kỹ năng CWP, chưa có thế Expert trở lên. Kiểm tra offline bằng Forge thật đã đạt; chưa xác nhận gameplay trực tiếp.

## 1.1.1 — Huyền Nguyệt, bước 1

Thêm phái thứ tám và 6 thế Novice bằng Forge, có cấp I–III, màu bạc lam, icon trăng khuyết. Tạm trao thủ công, chưa chọn ngẫu nhiên cho NPC. Giữ ID bảy phái cũ. Kiểm tra biên dịch và bộ đọc Forge thật đã đạt; chưa chơi thử. Chưa chuyển Nguyệt Khuyết từ CWP. Xem [lộ trình 7 bước](HUYEN_NGUYET_ROADMAP_VI.md).

## Nền tảng

CombatGods/Võ Đạo Chiến Thần của Turbo: hệ thống võ đạo, trường phái/kỹ thuật, AI chiến đấu, quyết đấu, VFX pixel và các năng lực cảnh giới cao. Thông tin gốc được giữ trong README.md và mod.json.

## Thay đổi bổ sung đang có trong nhánh này

- Giới hạn Thiên Nhân có thể chỉnh 0–1000.
- Đổi tên cảnh giới cuối thành Chân Thần, giữ ID `sakuga_tier_primordial` để tương thích save.
- Điều kiện mặc định: 50.000 kinh nghiệm võ đạo, 50 năm ở Thiên Nhân, kết liễu 10 Thiên Nhân khác nhau, từng sống dưới 0,5% máu và sống thêm 30 giây mô phỏng sau đòn địch hợp lệ.
- Cửu Trọng Lôi Kiếp: NPC đứng im chịu chín đợt sét tăng dần; sống qua đợt cuối mới tự đột phá. Có lưu trạng thái trong dữ liệu NPC, cấu hình độ khó và hiệu ứng.
- Công tắc tự giao chiến Chân Thần/hủy thế giới; mặc định mới là tắt, cấu hình cũ vẫn giữ giá trị đã lưu. Tắt có thể dừng sự kiện đang diễn ra.
- Tương thích tùy chọn với trạng thái bí cảnh/đại hội/truy sát của CWP.

Chi tiết: [CHAN_THAN_VI.md](CHAN_THAN_VI.md). Bảng thuật ngữ: [GLOSSARY_VI.md](GLOSSARY_VI.md).

## Kiểm tra

`Verify-Compile.ps1 -CheckSettings` biên dịch dựa trên assembly WorldBox/NML cài tại máy phát triển, kiểm tra quy tắc lôi kiếp và cấu hình. Không chứa DLL game trong repository. Script hiện dùng đường dẫn ổ E; khi clone sang máy khác cần điều chỉnh.

Kiểm tra offline không thay thế xác minh trực tiếp AI, VFX và load save trong WorldBox. Các thay đổi CWP, gồm Nguyệt Khuyết Kiếm Điển và registry pháp bảo, nằm ở repository CWP riêng.
