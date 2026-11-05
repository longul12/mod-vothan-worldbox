# Huyền Nguyệt phái — lộ trình triển khai

Ngày bắt đầu: 2026-11-05. Đây là phần mở rộng trong Võ Thần; giữ nguồn gốc engine của Turbo.

## Các bước và điều kiện hoàn thành

| Bước | Phạm vi | Trạng thái |
|---|---|---|
| 1 | Đăng ký phái, màu bạc lam, icon, tên, 6 thế Novice cấp I–III | Đã viết; kiểm tra offline, chưa thử game |
| 2 | 6 thế Adept, kiểm tra phản ứng và chuỗi cận chiến/tầm xa | Đã viết; kiểm tra offline, chưa thử game |
| 3 | 6 thế Expert, hất tung, truy kích, phân ảnh và hộ thể | Chưa làm |
| 4 | 6 thế Master, chuyển Nguyệt Khuyết từ CWP sang động tác Võ Thần | Chưa làm |
| 5 | 6 thế Grandmaster, trận pháp và biến thể kiếm khí | Chưa làm |
| 6 | 6 thế God, mặt trăng giáng xuống và tuyệt kỹ riêng | Chưa làm |
| 7 | Cân bằng toàn phái, học tự nhiên, giao diện, save/load và thử tải | Chưa làm |

Mỗi bước cần biên dịch, kiểm tra công thức và ID, sau đó thử trong game trước khi coi là hoàn tất gameplay. Commit riêng từng đợt. Không tuyên bố ngang sức phái gốc khi chưa đấu thử.

## Danh sách mục tiêu: 36 thế

| Cấp mở khóa | Sáu thế |
|---|---|
| Novice | Nguyệt Quang Chưởng; Tiểu Nguyệt Nhận; Nguyệt Ảnh Bộ; Ngân Huy Hộ Thể; Tân Nguyệt Trảm; Hồi Quang Phản Chưởng |
| Adept | Song Nguyệt Chưởng; Liên Hoàn Nguyệt Nhận; Hồi Hoàn Nguyệt Nhận; Đạp Nguyệt Truy Phong; Bán Nguyệt Hoành Tảo; Nguyệt Kính Phản Kích |
| Expert | Ngưng Nguyệt Chưởng; Tam Điệp Nguyệt Nhận; Thăng Nguyệt Kích; Nguyệt Ảnh Phân Thân; Nguyệt Hoa Liên Bạo; Ngân Nguyệt Kết Giới |
| Master | Nguyệt Khuyết; Vọng Nguyệt Đại Chưởng; Nguyệt Luân Hồi Chuyển; Nguyệt Ảnh Liên Hoàn; Trăng Tàn Phá Giáp; Nguyệt Lạc Truy Kích |
| Grandmaster | Tam Trọng Nguyệt Khuyết; Minh Nguyệt Chiếu Không; Nguyệt Ảnh Trận; Nguyệt Luân Thiên Vũ; Kính Nguyệt Hoán Vị; Nguyệt Thực Chấn |
| God | Thiên Nguyệt Trụy Thế; Cửu Nguyệt Liên Châu; Nguyệt Thần Cự Chưởng; Thiên Mạc Nguyệt Nhận; Vĩnh Dạ Minh Nguyệt; Nhất Nguyệt Khai Thiên |

## Kiến trúc và lưu dữ liệu

- Enum School.HuyenNguyet nối cuối enum, giữ nguyên số ID các phái cũ.
- Trait phái: sakuga_school_huyen_nguyet. Sáu thế đầu dùng tiền tố sakuga_hn_.
- Forge/huyen_nguyet.json đi qua Forge, Brain, Choreo, Combat, cấp I–III và hồi chiêu sẵn có. Không thêm vòng AI hoặc quét toàn bản đồ.
- Cảnh giới mở thế mới; mỗi thế nâng theo Technique.Level của Võ Thần, giữ quy tắc God hiện có.
- Quyền học là trait phái/trait chiêu của engine, không tạo bộ dữ liệu tu luyện trùng lặp.
- Chưa chỉnh CWP: Nguyệt Khuyết do kiếm cấp vẫn chạy bản cũ. Khi bước 4 hoàn thành phải chuyển quyền và tắt bản CWP khi backend mới khả dụng, chỉ một hồi chiêu/đòn sát thương.
- Chưa tự phân phái: trọng số chọn Huyền Nguyệt bằng 0 cho NPC mới, các thành viên trao thủ công vẫn được engine nhận biết. Tắt tùy chọn Combat schools có thể cho NPC dùng mọi phái theo hành vi gốc.

## Bản đầu và giới hạn hiện tại

- Màu ánh bạc lam, dùng động tác/VFX gốc qua Element.School. Icon phái hình trăng khuyết tự dựng bằng pixel; icon từng thế tạm dùng icon phái qua fallback Forge.
- Hộ thể hiện là phản đòn ngắn (guard), chưa phải lá chắn tích lũy. Tân Nguyệt Trảm hiện là combo đánh một mục tiêu, chưa có vùng chém rộng riêng.
- Nguyệt Ảnh Bộ là lùi phản kích do planner chọn, chưa phải phản xạ né mọi projectile.
- Sát thương cơ bản gần công thức phái gốc; hồi 4–9 giây trước các hệ số của engine, mana 6–16. Chưa kết luận cân bằng thực tế.
- Đã có thêm 6 thế Adept: Song Nguyệt Chưởng; Liên Hoàn Nguyệt Nhận; Hồi Hoàn Nguyệt Nhận; Đạp Nguyệt Truy Phong; Bán Nguyệt Hoành Tảo; Nguyệt Kính Phản Kích.
- Adept dùng mana 10–35, hồi 5–10 giây trước hệ số engine. Hồi Hoàn có thể trúng mỗi chiều một lần, hệ số cơ bản 0,85/lượt; không có cơ chế trúng lặp vô hạn. Bán Nguyệt quét phía trước, planner ưu tiên ít nhất 2 địch gần; Nguyệt Kính chỉ phản đòn gần.
- Chưa có mặt trăng khổng lồ, các chiêu Expert trở lên hoặc thay thế kỹ năng CWP.

## Kiểm tra trong game

1. Khởi động lại game; cho NPC một cảnh giới Võ Thần rồi trao Huyền Nguyệt phái qua bảng trait.
2. Để thử riêng, dùng NPC chỉ có một trait phái này; NPC có nhiều phái vẫn có thể chọn chiêu phái khác.
3. Đánh thử ở gần/xa; quan sát chưởng, nguyệt nhận, lùi bước, hai phản ứng phòng thủ.
4. So sánh cùng NPC ở Novice, Adept, Expert để kiểm tra cấp I–III: Novice có 6 thế; Adept trở lên có thêm 6 thế. Chưa có thế riêng mở từ Expert trở lên.
5. Save/load, xóa trait phái, đổi world, đấu giải và bí cảnh; kiểm tra không giữ hiệu ứng hoặc cấp quyền sai.

Chỉ mở chọn ngẫu nhiên và công bố bộ phái đầy đủ sau khi hoàn thiện đủ sáu bậc cùng kiểm tra gameplay.
