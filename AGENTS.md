# Quy ước làm việc — Võ Đạo Chiến Thần

Repository: https://github.com/longul12/mod-vothan-worldbox.git

- Người dùng yêu cầu lưu tiến độ vào Git. Sau mỗi đợt thay đổi hoàn chỉnh: kiểm tra phù hợp, commit rõ nội dung, push lên nhánh theo dõi trên origin và báo mã commit/tình trạng push.
- Không force push, sửa lịch sử đã công bố hoặc ghi đè thay đổi ngoài nhiệm vụ. Fetch và xem thay đổi remote trước khi tích hợp nếu cần.
- Repository này quản lý Võ Đạo Chiến Thần/CombatGods. CultivationWarPrototype được quản lý riêng tại https://github.com/longul12/mod-CultivationWarPrototype-worldbox.git.
- Giữ thông tin tác giả gốc Turbo và phân biệt thay đổi bổ sung của nhánh này với cơ chế gốc.
- Không commit DLL game/NML, build output, save game, log, thông tin xác thực hoặc thư mục dev dùng để capture/debug.
- Chạy `powershell -NoProfile -ExecutionPolicy Bypass -File ./Verify-Compile.ps1 -CheckSettings`. Đường dẫn WorldBox/NML trong script thuộc máy Windows hiện tại; máy khác phải điều chỉnh.
- Phân biệt kiểm tra offline với thử trực tiếp trong game; không khẳng định VFX/save-load đã được xác minh trực tiếp nếu chưa thực hiện.
- Khi chép mod vào game, loại trừ `.git` và các file riêng của Git. Không đồng bộ cả metadata repository vào thư mục mod đang chạy.
