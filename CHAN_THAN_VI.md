# Thiên Nhân và Chân Thần

Trong Cài đặt Võ Đạo Chiến Thần, nhóm tiến triển:

- Giới hạn Thiên Nhân: 0–1000, giữ nguyên giá trị đang lưu.
- Tự đột phá Chân Thần: bật/tắt tiến triển tự nhiên từ Thiên Nhân.
- Giới hạn Chân Thần: mặc định 10, chỉnh 0–1000. Tính tất cả Chân Thần đang sống, kể cả ban thủ công. Không thu hồi cảnh giới nếu giảm giới hạn.
- Kinh nghiệm: mặc định 50.000 kinh nghiệm võ đạo, chỉnh 1.000–1.000.000.
- Thời gian: mặc định 50 năm Thiên Nhân, chỉnh 1–1000. Nhân vật cũ bắt đầu ghi thời gian khi hệ thống nhận diện sau cập nhật.
- Chiến tích: mặc định kết liễu **10 Thiên Nhân khác nhau** khi bản thân đang là Thiên Nhân. Chỉnh 0–1000. Không tính Chân Thần, quái/hộ vệ/boss bí cảnh, đồng minh hoặc thí sinh đang đấu giải/khiêu chiến. Ghi nhớ ID để không đếm hai lần cùng nạn nhân. Chiến tích boss cũ không chuyển thành chiến tích này.
- Thập tử nhất sinh: mặc định bắt buộc. Còn sống **dưới 0,5% máu** sau đòn của kẻ địch, rồi sống thêm **30 giây thời gian thế giới**. Đúng 0,5% chưa đủ; 0 máu không được tính. Không tính tự gây sát thương, thi đấu, quái bí cảnh hoặc sét thiên kiếp. Chết hoặc đổi dạng trong lúc chờ sẽ hủy lần ghi nhận đang chờ. Có công tắc bỏ điều kiện này.
- Lôi Kiếp: sức mạnh 10–500%, mặc định 100%. Hiệu ứng tăng cường có công tắc riêng.

Không còn xác suất đột phá 5% mỗi năm. Trong lượt xét tiến triển hằng năm, người đủ điều kiện, ít nhất 95% máu, không giao chiến, không ở trong công trình/đang bay và không bận giải đấu, bí cảnh, truy sát sẽ bắt đầu thiên kiếp. Mỗi thế giới chỉ có một người độ kiếp tại một thời điểm để giới hạn tải hiệu ứng.

## Cửu Trọng Lôi Kiếp

NPC đứng im tại chỗ, khóa AI chiến đấu, di chuyển và kỹ năng chủ động do Võ Thần điều khiển. Hồi phục, trạng thái và hiệu ứng trait bị động gốc vẫn được cập nhật. Các mod bên ngoài tự dịch chuyển hoặc thay đổi NPC có thể can thiệp ngoài phạm vi kiểm soát này.

Chuẩn bị 6 giây, sau đó chín đợt sét. Trước mỗi đợt có vòng báo 1,2 giây; đợt chín tụ lực 2,5 giây. Nghỉ 3,5 giây giữa các đợt. Thời gian hiệu ứng dùng đồng hồ động tác của Võ Thần, tạm dừng cùng game và có giới hạn tốc độ để nhìn rõ ở tốc độ cao.

Sát thương gốc tăng lần lượt 6%, 8%, 10%, 12%, 14%, 16%, 19%, 22%, 30% của lượng máu tối đa chụp tại lúc bắt đầu, với cơ sở máu bị chặn trong khoảng 3500–8000. Nhân hệ số sức mạnh menu, rồi đi qua xử lý giáp/giảm sát thương và khiên của game/mod. Ví dụ cơ sở 4000, độ mạnh 100%: tổng 5480 sát thương trước giảm trừ. Không tự tăng độ khó khi NPC hồi phục hoặc đổi chỉ số trong lúc độ kiếp.

Sét gốc `fx_lightning_small/medium/big` kết hợp mây pixel mới, sét phân nhánh, vòng điện và bụi. Hiệu ứng không gọi quyền năng sét gây phá địa hình; sát thương trực tiếp chỉ tính một lần lên người độ kiếp mỗi đợt. Không rung màn hình, không chớp sáng toàn màn hình. Mây được trả về pool khi kết thúc.

Sống qua chín đợt và khoảng kết thúc 1 giây mới được ban Chân Thần và hồi đầy máu. Không hồi sinh người chết. Nếu bị hủy khi còn sống, giữ Thiên Nhân và chờ 20 năm để thử lại; chiến tích và lĩnh ngộ đã đạt được giữ nguyên.

Trạng thái, số đợt đã chịu, cơ sở sát thương và mức khó được lưu trong dữ liệu NPC. Nạp save sẽ có 3 giây ổn định rồi tiếp tục từ đợt chưa chịu; không đánh lại đợt đã ghi nhận. Đổi world chỉ giải phóng tham chiếu runtime. NPC đang độ kiếp không được tuyển vào bí cảnh/giải đấu của CultivationWarPrototype.

Tắt tự đột phá, tắt tiến triển chung, hạ cảnh giới tự nhiên tối đa dưới Thiên Nhân hoặc đặt giới hạn Chân Thần bằng 0 sẽ dừng thiên kiếp khi tiếp tục mô phỏng. Ban trait thủ công vẫn bỏ qua toàn bộ điều kiện và thiên kiếp. Bản lưu cũ giữ nguyên Chân Thần đã có; các điều kiện chiến tích và thoát chết mới chỉ ghi nhận sau cập nhật.

Trong nhóm cân bằng có **Tự giao chiến Chân Thần (hủy thế giới)**. Mặc định mới là tắt; cấu hình cũ vẫn giữ lựa chọn đã lưu, cần tự tắt công tắc này. Tắt dừng cả sự kiện đang chạy, nhưng không khôi phục thiệt hại đã gây ra. Giao chiến thông thường vẫn hoạt động.

ID trait vẫn là `sakuga_tier_primordial` để tương thích bản lưu và mod khác; tên hiển thị đổi thành Chân Thần.
