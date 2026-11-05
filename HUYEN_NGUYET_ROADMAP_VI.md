# Huyền Nguyệt phái — lộ trình triển khai

Ngày bắt đầu: 2026-11-05. Đây là phần mở rộng trong Võ Thần; giữ nguồn gốc engine của Turbo.

## Các bước và điều kiện hoàn thành

| Bước | Phạm vi | Trạng thái |
|---|---|---|
| 1 | Đăng ký phái, màu bạc lam, icon, tên, 6 thế Novice cấp I–III | Đã viết; kiểm tra offline, chưa thử game |
| 2 | 6 thế Adept, kiểm tra phản ứng và chuỗi cận chiến/tầm xa | Đã viết; kiểm tra offline, chưa thử game |
| 3 | 6 thế Expert, hất tung, nguyệt nhận, phân ảnh và hộ thể | Đã viết; kiểm tra offline, chưa thử game |
| 4 | 6 thế Master, chuyển Nguyệt Khuyết từ CWP sang động tác Võ Thần | Đã viết; kiểm tra offline, chưa thử game |
| 5 | 6 thế Grandmaster, phân ảnh và biến thể kiếm khí/vùng đánh | Đã viết; kiểm tra offline, chưa thử game |
| 6 | 6 thế God, mặt trăng giáng xuống và tuyệt kỹ riêng | Đã viết; kiểm tra offline, chưa thử game |
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
- Võ Thần 1.1.4 có MoonSwordApi; CWP 0.16.0 phát hiện API để dừng scheduler/projectile cũ và chỉ đối soát quyền từ kiếm. Không tự thêm trait chiêu vĩnh viễn. Có kiếm được ít nhất cấp I; học phái lấy cấp cao hơn. Mất kiếm không xóa kỹ năng học được. Cần cập nhật cả hai mod cùng đợt.
- Chưa tự phân phái: trọng số chọn Huyền Nguyệt bằng 0 cho NPC mới, các thành viên trao thủ công vẫn được engine nhận biết. Tắt tùy chọn Combat schools có thể cho NPC dùng mọi phái theo hành vi gốc.

## Bản đầu và giới hạn hiện tại

- Màu ánh bạc lam, dùng động tác/VFX gốc qua Element.School. Icon phái hình trăng khuyết tự dựng bằng pixel; icon từng thế tạm dùng icon phái qua fallback Forge.
- Hộ thể hiện là phản đòn ngắn (guard), chưa phải lá chắn tích lũy. Tân Nguyệt Trảm hiện là combo đánh một mục tiêu, chưa có vùng chém rộng riêng.
- Nguyệt Ảnh Bộ là lùi phản kích do planner chọn, chưa phải phản xạ né mọi projectile.
- Sát thương cơ bản gần công thức phái gốc; hồi 4–9 giây trước các hệ số của engine, mana 6–16. Chưa kết luận cân bằng thực tế.
- Đã có thêm 6 thế Adept: Song Nguyệt Chưởng; Liên Hoàn Nguyệt Nhận; Hồi Hoàn Nguyệt Nhận; Đạp Nguyệt Truy Phong; Bán Nguyệt Hoành Tảo; Nguyệt Kính Phản Kích.
- Adept dùng mana 10–35, hồi 5–10 giây trước hệ số engine. Hồi Hoàn có thể trúng mỗi chiều một lần, hệ số cơ bản 0,85/lượt; không có cơ chế trúng lặp vô hạn. Bán Nguyệt quét phía trước, planner ưu tiên ít nhất 2 địch gần; Nguyệt Kính chỉ phản đòn gần.
- Đã có 6 thế Expert: Ngưng Nguyệt Chưởng, Tam Điệp Nguyệt Nhận, Thăng Nguyệt Kích, Nguyệt Ảnh Phân Thân, Nguyệt Hoa Liên Bạo và Ngân Nguyệt Kết Giới.
- Expert: mana 16–48, hồi 6–11 giây trước hệ số engine. Phân thân dùng clones chia tổng sát thương cho các ảnh, không sinh Actor thật. Liên Bạo dùng nguyệt cầu bay vòng cung rồi nổ nhỏ, chưa phải bãi mìn được đặt sẵn; crater=0. Kết Giới là phản ứng guard và xung kích, chưa có khiên hấp thụ kéo dài. Thăng Nguyệt tạo Link.Launched để các nguyệt nhận sẵn có nối thế, chưa thêm thế truy kích cận chiến trên không riêng.
- Đã có 6 thế Master: Nguyệt Khuyết viết C# riêng, Vọng Nguyệt Đại Chưởng, Nguyệt Luân Hồi Chuyển, Nguyệt Ảnh Liên Hoàn, Trăng Tàn Phá Giáp, Nguyệt Lạc Truy Kích.
- Nguyệt Khuyết native dùng sprite crescent 26/9 trắng bạc, dư ảnh 0,07 giây; tốc độ 16, tầm 12, xuyên tối đa 8 địch, giảm 10% mỗi mục tiêu sàn 65%. Cấp I–III: hệ số 1,4/1,7/2,0, mana 24/36/52; hồi tối thiểu 10/12/14 giây thế giới từ lúc bắt đầu (bị ngắt vẫn tốn). Native Brain có thể áp dụng hồi chiêu dài hơn. Đồng hồ khác fallback CWP cũ.
- Chỉ Nguyệt Khuyết bật cờ LunarCrescent và falloff của ShotSpec; mọi projectile cũ giữ mặc định không suy giảm. Kiếm khí có thể bị phản/cản theo engine và vùng đánh chịu hệ số hình thể. Dùng chung pool hiệu ứng native.
- Các thế Master khác dùng mana 24–58 và hồi 8–14 giây trước hệ số engine. Nguyệt Luân hiện là hai vòng xung kích, chưa có luân bay quỹ đạo riêng. Trăng Tàn hiện là đòn nặng, chưa xuyên phần trăm giáp. Nguyệt Lạc nối Link.Launched, không đào hố.
- Đã có 6 thế Grandmaster: Tam Trọng Nguyệt Khuyết, Minh Nguyệt Chiếu Không, Nguyệt Ảnh Trận, Nguyệt Luân Thiên Vũ, Kính Nguyệt Hoán Vị, Nguyệt Thực Chấn. Tổng 30 thế (29 Forge + 1 C#).
- Grandmaster dùng mana 32–84, hồi 11–21 giây trước hệ số engine. Tam Trọng luôn ba projectile riêng (mỗi projectile xuyên tối đa 3 địch, decay 0,9/sàn 0,65), không tăng thành 5 trăng lớn khi lên cấp III. Thiên Vũ dùng luân xoay native, loạt chính tối đa 5 ở III, có thêm tối đa 2 nhát bồi nhỏ.
- Minh Nguyệt hiện dùng cột ánh sáng có cảnh báo 0,45 giây và ba nhịp sát thương; chưa có sprite mặt trăng treo riêng. Nguyệt Ảnh Trận bao quanh một mục tiêu bằng phân ảnh, chưa là trận pháp đánh toàn vùng. Kính Nguyệt né rồi blink ra sau lưng, chưa hoán đổi hai Actor thật.
- Nguyệt Thực dùng lực hút ngắn rồi phát nổ/hất tung. Minh Nguyệt và Nguyệt Thực đặt terrain=0 để không gọi Ruin.Blast/Quake; dấu va chạm và bụi đá còn là hiệu ứng trang trí. Các kỹ thuật cũ vẫn giữ mặc định terrain=true.
- Forge hỗ trợ lunar/decay/floor cho projectile; mặc định giữ nguyên projectile cũ. Các chiêu mới dùng pool và vòng cập nhật native, không thêm vòng quét toàn bản đồ.
- God có 6 thế: Thiên Nguyệt Trụy Thế, Cửu Nguyệt Liên Châu, Nguyệt Thần Cự Chưởng, Thiên Mạc Nguyệt Nhận, Vĩnh Dạ Minh Nguyệt và Nhất Nguyệt Khai Thiên. Tổng 36 thế: 33 Forge, 3 kỹ thuật C#.
- Trụy Thế có sprite mặt trăng thủ tục 81×81 riêng (tạo/cache một lần), không phải thiên thạch. Sau tụ lực 1,1 giây, vùng đất báo trước 0,9 giây rồi trăng rơi 0,65 giây; điểm rơi cố định để né. Radius 5,5 ô, hệ số I–III 2,8/3,3/3,8, mana 70/100/130 và hồi cơ sở 35/45/55 giây. Không phá địa hình/nhà.
- Vĩnh Dạ tạo mặt trăng treo trên vùng cố định radius 4,5 trong 4,4 giây, bốn nhịp gây sát thương; mỗi nhịp hồi 1% max HP làm tròn xuống cho chủ nếu còn trong vùng (tối đa 4%, không hồi đồng minh). Mana 60/90/120, hồi 45/55/65 giây trước hệ số engine.
- Hai hazard mới dùng chung giới hạn tối đa 6 đang hoạt động, mỗi nhịp xét tối đa 64 ứng viên và 24 mục tiêu. Chủ chết/đổi dạng thì hủy. Areas.Clear thu hồi khi đổi world; particle Held kiểm tra generation trước khi cập nhật/xóa. Không thêm vòng quét toàn map.
- Cửu Nguyệt dùng đúng 9 nguyệt cầu bay liên tiếp, chưa dựng đội hình chín mặt trăng sau lưng. Cự Chưởng dùng quyền ảnh áp lực native, chưa có bàn tay năm ngón riêng. Thiên Mạc cho nhân vật bay lên rồi phóng 5 nhận xuống (thêm tối đa 2 nhát bồi), chưa tạo cổng trên trời. Khai Thiên dùng sprite crescent lớn hiện có, hệ số cao và tụ lực lâu; chưa có sprite siêu lớn riêng.
- God theo engine hiện có thường thi triển các thế ở III. Các mức I–II vẫn có định nghĩa cho trường hợp trao trait kỹ thuật riêng. Các số hồi/mana nêu ở đây là cơ sở trước hệ số engine.
- Đã đủ danh sách dự kiến 36 thế, nhưng bước 7 cân bằng/gameplay, icon từng chiêu, các biến thể hình ảnh còn thiếu và học tự nhiên chưa hoàn tất. Chưa kết luận ngang sức các phái gốc.

## Kiểm tra trong game

1. Khởi động lại game; cho NPC một cảnh giới Võ Thần rồi trao Huyền Nguyệt phái qua bảng trait.
2. Để thử riêng, dùng NPC chỉ có một trait phái này; NPC có nhiều phái vẫn có thể chọn chiêu phái khác.
3. Đánh thử ở gần/xa; quan sát chưởng, nguyệt nhận, lùi bước, hai phản ứng phòng thủ.
4. Novice/Adept/Expert/Master/Grandmaster lần lượt mở 6/12/18/24/30 thế. Thử Thăng Nguyệt hất tung rồi nối Nguyệt Lạc, đánh đơn xem phân ảnh, đánh nhóm xem Liên Bạo và Nguyệt Luân. Grandmaster mở 6 thế mới ở I; God nâng lên III theo engine.
6. Với CWP 0.16.0: trao kiếm cho NPC chưa học phái để thử quyền cấp I; trao phái và Master/Grandmaster/God để thử I/II/III. Thay kiếm phải mất quyền do kiếm cấp nhưng giữ quyền đã học. Quan sát chỉ một kiếm khí mỗi lần thi triển, hồi sau save/load không bị làm mới, và charge bị choáng vẫn ngắt được. Các bài này chưa được chạy trực tiếp trong game.
7. Grandmaster: thử Tam Trọng đúng ba lưỡi, Chiếu Không có vòng báo trước, Nguyệt Thực kéo rồi hất địch. So sánh địa hình/nhà trước sau hai chiêu vùng mới. Theo dõi số particle, thời gian frame và tương tác đại hội trong game trước khi kết luận hiệu năng/cân bằng.
8. God: thử giao chiến nhóm để planner chọn chiêu vùng. Trụy Thế phải có mặt trăng và vòng báo, chỉ đánh một lần khi chạm đất. Vĩnh Dạ phải có đúng 4 nhịp và chỉ hồi chủ khi ở trong vòng. Kiểm tra pause, owner chết/biến ghost, đổi world giữa chiêu, 6 hazard đồng thời và bảo vệ thí sinh chờ. Chưa thực hiện các bài gameplay này.
5. Save/load, xóa trait phái, đổi world, đấu giải và bí cảnh; kiểm tra không giữ hiệu ứng hoặc cấp quyền sai.

Chỉ mở chọn ngẫu nhiên và công bố bộ phái đầy đủ sau khi hoàn thiện đủ sáu bậc cùng kiểm tra gameplay.
