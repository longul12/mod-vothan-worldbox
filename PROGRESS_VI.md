# Tiến độ nhánh Võ Thần

Mốc đầu tiên đưa lên Git: **1.1.0**.

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
