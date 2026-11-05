# Tiến độ nhánh Võ Thần

Mốc đầu tiên đưa lên Git: **1.1.0**.

## 1.1.5 — Huyền Nguyệt, bước 5

Thêm 6 thế Grandmaster, tổng 30 thế. Có ba nguyệt khuyết lớn liên tiếp, nguyệt quang giáng xuống có cảnh báo, phân ảnh một mục tiêu, nguyệt luân hình quạt, né ra sau phản kích và nguyệt thực hút/nổ. Thêm lựa chọn terrain=false cho BlackHole/SkyBeam, giữ mặc định true của mọi chiêu cũ. Forge đọc được lunar/decay/floor cho projectile. Chiêu mới không thêm vòng AI riêng, dùng tài nguyên native; chưa đo hiệu năng gameplay. Kiểm tra biên dịch và parser thật cùng giới hạn chi phí, số trăng, vùng đánh và mặc định tương thích đã đạt. Chưa có sprite mặt trăng treo riêng, hoán đổi Actor thật hoặc trận pháp nhiều mục tiêu; bước tiếp theo là 6 thế God và mặt trăng giáng xuống.

## 1.1.4 — Huyền Nguyệt, bước 4

Tổng 24 thế: 23 Forge và Nguyệt Khuyết C# native. Thêm API để CWP 0.16.0 chỉ cấp quyền từ kiếm, không chạy hai bộ chiêu. Nguyệt Khuyết dùng chu trình Brain/Choreo, mana và phản projectile của engine; giữ crescent trắng lớn, dư ảnh, 8 địch và falloff 0,9/sàn 0,65. Cấp I–III hệ số 1,4/1,7/2,0; hồi tối thiểu 10/12/14 giây thế giới từ lúc bắt đầu kể cả bị ngắt. Lưu cooldown actor, nhập remaining CWP lần đầu. Năm thế Master khác bổ sung chưởng lớn, xung kích, phân ảnh và truy kích trên không. Trăng Tàn chưa có xuyên giáp phần trăm; Nguyệt Luân chưa có quỹ đạo độc lập. Biên dịch và kiểm tra offline hai mod đạt; cần thử gameplay chung.

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
