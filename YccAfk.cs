using System.Reflection;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace YccAfk;

// Ayarlar eski AFKManager'ın sunucudaki ayarlarıyla aynı başlar (configs/plugins/AFKManager/AFKManager.json).
public class Ayarlar : BasePluginConfig
{
    [JsonPropertyName("KontrolAraligi")] public float KontrolAraligi { get; set; } = 5;

    // AFK: oyuncu yerinden ve bakış açısından hiç oynamazsa her UyariAraligi'nda uyarılır, CezaUyari'dan sonra ceza.
    // Ceza: 0 = öldür, 1 = öldür + izleyiciye al, 2 = at
    [JsonPropertyName("AfkUyariAraligi")] public float AfkUyariAraligi { get; set; } = 5;
    [JsonPropertyName("AfkCezaUyari")] public int AfkCezaUyari { get; set; } = 3;
    [JsonPropertyName("AfkCeza")] public int AfkCeza { get; set; } = 1;
    [JsonPropertyName("AfkMuaf")] public List<string> AfkMuaf { get; set; } = new() { "@css/root", "@css/ban" };

    // İzleyicide bekleyen: sunucuda en az IzleyiciEnAzOyuncu kişi varsa her IzleyiciUyariAraligi'nda uyarı, sonra atılır
    [JsonPropertyName("IzleyiciUyariAraligi")] public float IzleyiciUyariAraligi { get; set; } = 20;
    [JsonPropertyName("IzleyiciAtUyari")] public int IzleyiciAtUyari { get; set; } = 5;
    [JsonPropertyName("IzleyiciEnAzOyuncu")] public int IzleyiciEnAzOyuncu { get; set; } = 5;
    [JsonPropertyName("IzleyiciMuaf")] public List<string> IzleyiciMuaf { get; set; } = new() { "@css/root", "@css/ban" };

    // Kamp: aynı noktanın KampYaricap birimi içinde kalan uyarılır, sonra ceza (0 = öldür, 1 = tokat)
    [JsonPropertyName("KampYaricap")] public float KampYaricap { get; set; } = 130;
    [JsonPropertyName("KampUyariAraligi")] public float KampUyariAraligi { get; set; } = 10;
    [JsonPropertyName("KampCezaUyari")] public int KampCezaUyari { get; set; } = 3;
    [JsonPropertyName("KampCeza")] public int KampCeza { get; set; } = 1;
    [JsonPropertyName("KampTokatHasar")] public int KampTokatHasar { get; set; } = 0;
    [JsonPropertyName("KampBombaKuruluykenSayma")] public bool KampBombaKuruluykenSayma { get; set; } = true;
    [JsonPropertyName("KampSayilmayanTakim")] public int KampSayilmayanTakim { get; set; } = 3;   // 3 = CT (savunan taraf)
    [JsonPropertyName("KampMuaf")] public List<string> KampMuaf { get; set; } = new() { "@css/root", "@css/ban" };

    [JsonPropertyName("IsinmadaSayma")] public bool IsinmadaSayma { get; set; } = false;
    [JsonPropertyName("UyariSesi")] public string UyariSesi { get; set; } = "ui/panorama/popup_reveal_01";
    [JsonPropertyName("On")] public string On { get; set; } = "{PURPLE}[AFK]{DEFAULT} »";
}

// AFK, izleyicide bekleme ve kamp yönetimi (kullanıcı 2026-11-15: kalan yabancı eklentiler de kendi kodumuz olsun;
// eski AFKManager'ın yerine, aynı ayar ve davranışla, sıfırdan).
public class YccAfk : BasePlugin, IPluginConfig<Ayarlar>
{
    public override string ModuleName => "YccAfk";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Claude";
    public override string ModuleDescription => "AFK, izleyicide bekleme ve kamp yönetimi";

    public Ayarlar Config { get; set; } = new();
    public void OnConfigParsed(Ayarlar config) => Config = config;

    private class Durum
    {
        public Vector? Konum; public QAngle? Bakis;
        public float AfkSure; public int AfkUyari;
        public float IzleyiciSure; public int IzleyiciUyari;
        public Vector? KampMerkez; public float KampSure; public int KampUyari;
    }

    private readonly Dictionary<int, Durum> _d = new();   // slot → durum
    private static readonly Random Zar = new();

    public override void Load(bool hotReload)
    {
        var aralik = Math.Max(1, Config.KontrolAraligi);
        AddTimer(aralik, () => Kontrol(aralik), TimerFlags.REPEAT);
        RegisterListener<Listeners.OnClientDisconnect>(slot => _d.Remove(slot));
        RegisterListener<Listeners.OnMapStart>(_ => _d.Clear());
        // Yeni doğuşta sayaçlar sıfırlanır (izleyici sayacı hariç)
        RegisterEventHandler<EventPlayerSpawn>((e, _) =>
        {
            if (e.Userid is { IsValid: true } p && _d.TryGetValue(p.Slot, out var d))
            { d.Konum = null; d.Bakis = null; d.AfkSure = 0; d.AfkUyari = 0; d.KampMerkez = null; d.KampSure = 0; d.KampUyari = 0; }
            return HookResult.Continue;
        });
    }

    private static bool Gercek(CCSPlayerController? p) => p != null && p.IsValid && !p.IsBot && !p.IsHLTV;
    private static bool Muaf(CCSPlayerController p, List<string> yetki) => yetki.Count > 0 && AdminManager.PlayerHasPermissions(p, yetki.ToArray());

    private void Yaz(CCSPlayerController p, string s) => p.PrintToChat(" " + Renk(Config.On + " " + s));
    private void Duyur(string s) => Server.PrintToChatAll(" " + Renk(Config.On + " " + s));
    private void Ses(CCSPlayerController p) { if (Config.UyariSesi != "") p.ExecuteClientCommand("play " + Config.UyariSesi); }

    private void Kontrol(float aralik)
    {
        CCSGameRules? kurallar;
        try { kurallar = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules; }
        catch { return; }
        if (kurallar == null || kurallar.FreezePeriod || (Config.IsinmadaSayma && kurallar.WarmupPeriod)) return;

        var oyuncular = Utilities.GetPlayers().Where(Gercek).ToList();
        foreach (var p in oyuncular)
        {
            if (!_d.TryGetValue(p.Slot, out var d)) _d[p.Slot] = d = new Durum();

            if (p.Team == CsTeam.Spectator) { Izleyici(p, d, aralik, oyuncular.Count); continue; }
            d.IzleyiciSure = 0; d.IzleyiciUyari = 0;
            var pawn = p.PlayerPawn.Value;
            if (!p.PawnIsAlive || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.EyeAngles == null) continue;

            var konum = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
            var bakis = new QAngle(pawn.EyeAngles.X, pawn.EyeAngles.Y, pawn.EyeAngles.Z);
            var kipirdamadi = d.Konum != null && d.Bakis != null && Uzaklik(konum, d.Konum) < 1 &&
                              Math.Abs(bakis.X - d.Bakis.X) < 0.01 && Math.Abs(bakis.Y - d.Bakis.Y) < 0.01;
            d.Konum = konum; d.Bakis = bakis;

            if (kipirdamadi && !Muaf(p, Config.AfkMuaf)) Afk(p, pawn, d, aralik);
            else { d.AfkSure = 0; d.AfkUyari = 0; }

            if (d.AfkUyari == 0) Kamp(p, pawn, d, konum, aralik, kurallar);
        }
    }

    private void Afk(CCSPlayerController p, CCSPlayerPawn pawn, Durum d, float aralik)
    {
        d.AfkSure += aralik;
        if (d.AfkSure < Config.AfkUyariAraligi * (d.AfkUyari + 1)) return;
        d.AfkUyari++;
        if (d.AfkUyari > Config.AfkCezaUyari)
        {
            d.AfkSure = 0; d.AfkUyari = 0;
            switch (Config.AfkCeza)
            {
                case 0: Duyur($"{{GREY}}{p.PlayerName}{{DEFAULT}} AFK kaldığı için {{LIME}}öldürüldü{{DEFAULT}}."); pawn.CommitSuicide(false, true); break;
                case 2: Duyur($"{{GREY}}{p.PlayerName}{{DEFAULT}} AFK kaldığı için {{LIME}}sunucudan atıldı{{DEFAULT}}."); Server.ExecuteCommand($"kickid {p.UserId} \"AFK\""); break;
                default: Duyur($"{{GREY}}{p.PlayerName}{{DEFAULT}} AFK kaldığı için {{LIME}}izleyiciye alındı{{DEFAULT}}."); pawn.CommitSuicide(false, true); p.ChangeTeam(CsTeam.Spectator); break;
            }
            return;
        }
        var kalan = (Config.AfkCezaUyari - d.AfkUyari + 1) * Config.AfkUyariAraligi;
        var ne = Config.AfkCeza switch { 0 => "öldürüleceksin", 2 => "sunucudan atılacaksın", _ => "izleyiciye alınacaksın" };
        Yaz(p, $"{{DARKRED}}AFK{{DEFAULT}} görünüyorsun. Hareket etmezsen {{LIGHTBLUE}}{kalan:0}{{DEFAULT}} saniye içinde {{LIME}}{ne}{{DEFAULT}}.");
        Ses(p);
    }

    private void Izleyici(CCSPlayerController p, Durum d, float aralik, int toplam)
    {
        if (Config.IzleyiciAtUyari <= 0 || toplam < Config.IzleyiciEnAzOyuncu || Muaf(p, Config.IzleyiciMuaf)) { d.IzleyiciSure = 0; d.IzleyiciUyari = 0; return; }
        d.IzleyiciSure += aralik;
        if (d.IzleyiciSure < Config.IzleyiciUyariAraligi * (d.IzleyiciUyari + 1)) return;
        d.IzleyiciUyari++;
        if (d.IzleyiciUyari >= Config.IzleyiciAtUyari)
        {
            Duyur($"{{GREY}}{p.PlayerName}{{DEFAULT}} uzun süre izleyicide kaldığı için {{LIME}}sunucudan atıldı{{DEFAULT}}.");
            Server.ExecuteCommand($"kickid {p.UserId} \"Uzun sure izleyicide kaldin\"");
            return;
        }
        var kalan = (Config.IzleyiciAtUyari - d.IzleyiciUyari) * Config.IzleyiciUyariAraligi;
        Yaz(p, $"İzleyicide bekliyorsun. Bir takıma geçmezsen {{LIGHTBLUE}}{kalan:0}{{DEFAULT}} saniye içinde {{LIME}}sunucudan atılacaksın{{DEFAULT}}.");
        Ses(p);
    }

    private void Kamp(CCSPlayerController p, CCSPlayerPawn pawn, Durum d, Vector konum, float aralik, CCSGameRules kurallar)
    {
        if (Config.KampCezaUyari <= 0 || p.TeamNum == Config.KampSayilmayanTakim || (Config.KampBombaKuruluykenSayma && kurallar.BombPlanted) || Muaf(p, Config.KampMuaf))
        { d.KampMerkez = null; d.KampSure = 0; d.KampUyari = 0; return; }
        if (d.KampMerkez == null || Uzaklik(konum, d.KampMerkez) >= Config.KampYaricap)
        { d.KampMerkez = konum; d.KampSure = 0; d.KampUyari = 0; return; }

        d.KampSure += aralik;
        if (d.KampSure < Config.KampUyariAraligi * (d.KampUyari + 1)) return;
        d.KampUyari++;
        var yer = pawn.LastPlaceName ?? "";
        var silah = pawn.WeaponServices?.ActiveWeapon.Value?.DesignerName?.Replace("weapon_", "") ?? "";
        if (d.KampUyari > Config.KampCezaUyari)
        {
            d.KampSure = 0; d.KampUyari = 0; d.KampMerkez = null;
            if (Config.KampCeza == 0) { Duyur($"{{GREY}}{p.PlayerName}{{DEFAULT}} {{GREEN}}@{yer}{{DEFAULT}} bölgesinde {silah} ile kamp yaptığı için {{LIME}}öldürüldü{{DEFAULT}}."); pawn.CommitSuicide(false, true); }
            else { Duyur($"{{GREY}}{p.PlayerName}{{DEFAULT}} {{GREEN}}@{yer}{{DEFAULT}} bölgesinde {silah} ile kamp yaptığı için {{LIME}}tokatlandı{{DEFAULT}}."); Tokat(pawn, Config.KampTokatHasar); }
            return;
        }
        var kalan = (Config.KampCezaUyari - d.KampUyari + 1) * Config.KampUyariAraligi;
        Yaz(p, $"{{GREEN}}@{yer}{{DEFAULT}} bölgesinde kamp yapmayı bırak, yoksa {{LIGHTBLUE}}{kalan:0}{{DEFAULT}} saniye içinde {{LIME}}{(Config.KampCeza == 0 ? "öldürüleceksin" : "tokatlanacaksın")}{{DEFAULT}}.");
        Ses(p);
    }

    private static void Tokat(CCSPlayerPawn pawn, int hasar)
    {
        if (hasar > 0)
        {
            if (pawn.Health <= hasar) { pawn.CommitSuicide(false, true); return; }
            pawn.Health -= hasar;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }
        var v = pawn.AbsVelocity;
        pawn.Teleport(null, null, new Vector(v.X + Zar.Next(-200, 201), v.Y + Zar.Next(-200, 201), v.Z + Zar.Next(150, 301)));
    }

    private static float Uzaklik(Vector a, Vector b)
    {
        float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return MathF.Sqrt(x * x + y * y + z * z);
    }

    private static readonly Dictionary<string, string> Renkler = BuildRenkler();
    private static Dictionary<string, string> BuildRenkler()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in typeof(ChatColors).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (f.GetValue(null) is char c) d["{" + f.Name + "}"] = c.ToString();
        if (d.TryGetValue("{Grey}", out var g)) d["{GRAY}"] = g;
        return d;
    }
    private static string Renk(string s)
    {
        foreach (var (k, v) in Renkler) s = s.Replace(k, v, StringComparison.OrdinalIgnoreCase);
        return s;
    }
}
