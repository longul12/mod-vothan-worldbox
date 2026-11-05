using NeoModLoader.General;

namespace Sakuga
{
    internal static class HuyenNguyetTexts
    {
        [SakugaInit]
        private static void Init()
        {
            foreach(string lang in new[]{"en","cz","vi"})
            {
                LM.Add(lang,"trait_sakuga_school_huyen_nguyet","Huyền Nguyệt phái");
                LM.Add(lang,"sakuga_school_huyen_nguyet","Huyền Nguyệt phái");
                LM.Add(lang,"trait_sakuga_school_huyen_nguyet_info","Vận nguyệt lực thành chưởng, nguyệt nhận và thân pháp. Bản thử nghiệm có 12 thế: 6 Novice, 6 Adept; mỗi thế cấp I–III. Tạm trao trait thủ công; chưa tự phân phái.");
                LM.Add(lang,"trait_group_sakuga_moves_huyen_nguyet","Huyền Nguyệt · Nhập môn");
                LM.Add(lang,"trait_group_sakuga_moves_huyen_nguyet_high","Huyền Nguyệt · Cao giai");
            }
        }
    }
}
