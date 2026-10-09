namespace SentinelX.Core;

/// <summary>
/// 0.99 ┬Ě RADA NAPRAWY ÔÇö konkretny nast─Öpny krok po pora┼╝ce.
/// Zasada: rada ma by─ç WYKONYWALNA (co otw├│rz, co sprawd┼║, co zmie┼ä), a nie og├│lnikiem
/// typu ÔÇ×spr├│buj ponownieÔÇŁ. Mapowana po rodzinie akcji (prefiks typu) i tre┼Ťci b┼é─Ödu.
/// </summary>
public static class RecoveryAdvisor
{
    private static readonly (string Family, string Advice)[] ByFamily =
    [
        ("MEASURE_", "Pon├│w odczyt wprost (np. ÔÇ×ile mam RAM?ÔÇŁ) ÔÇö je┼Ťli znowu pusto, sprawd┼║ w Mened┼╝erze zada┼ä, czy liczniki systemu odpowiadaj─ů."),
        ("OPEN_", "Sprawd┼║, czy aplikacja istnieje w Start; je┼Ťli tak, uruchom j─ů r─Öcznie i podaj mi jej dok┼éadn─ů nazw─Ö ÔÇö dodam j─ů do rozpoznawanych."),
        ("CLOSE_", "Sprawd┼║ nazw─Ö procesu w Mened┼╝erze zada┼ä (zak┼éadka Szczeg├│┼éy) i podaj j─ů wprost ÔÇö zamkam po nazwie procesu, nie po oknie."),
        ("CLEANUP", "Otw├│rz Ustawienia Ôćĺ System Ôćĺ Pami─Ö─ç i sprawd┼║ folder ÔÇ×Pliki tymczasoweÔÇŁ; mi powiedz, ile miejsca pokazuje ÔÇö por├│wnam z moim dowodem."),
        ("NETWORK_", "Sprawd┼║, czy inne urz─ůdzenia maj─ů internet; je┼Ťli tak ÔÇö zrestartuj router i popro┼Ť mnie o ponown─ů diagnostyk─Ö sieci."),
        ("FILE_", "Podaj pe┼én─ů ┼Ťcie┼╝k─Ö pliku i sprawd┼║, czy nie otwierasz go w innym programie ÔÇö zamkni─Öcie blokady zwykle wystarcza."),
        ("SMARTHOME_", "Sprawd┼║, czy urz─ůdzenie odpowiada w aplikacji producenta; dopiero potem ponawiaj polecenie ÔÇö nie powtarzam polece┼ä na ┼Ťlepo."),
        ("MEMORY_", "Wypisz notatk─Ö jeszcze raz kr├│cej ÔÇö d┼éugie notatki tn─ů si─Ö przy zapisie; sprawd┼║ j─ů potem na stronie Pami─Ö─ç."),
        ("CALL_", "Uruchom aplikacj─Ö pomocnicz─ů na telefonie i po┼é─ůcz most (ÔÇ×w┼é─ůcz most telefonicznyÔÇŁ); sprawd┼║ zasi─Ög karty SIM. Rozmowa idzie przez G┼üO┼ÜNIK telefonu ÔÇö nie przyk┼éadaj go do ucha, gdy Sentinel rozmawia."),
    ];

    /// <summary>Wykonywalna rada naprawy; nigdy pusta ÔÇö zawsze co┼Ť konkretnego.</summary>
    public static string Advise(string actionType, string error)
    {
        foreach ((string family, string advice) in ByFamily)
        {
            if (actionType.StartsWith(family, StringComparison.Ordinal))
            {
                return advice + " (przyczyna: " + Short(error) + ")";
            }
        }
        if (RetryAdvisor.TransientCause(error) is { Length: > 0 } cause)
        {
            return "Przyczyna wygl─ůda na chwilow─ů (" + cause + ") ÔÇö odczekaj chwil─Ö i pon├│w to samo polecenie.";
        }
        return "Sformu┼éuj polecenie wprost i konkretnie (nazwa + dzia┼éanie); historia dowod├│w po lewej poka┼╝e, na kt├│rym kroku stan─Ö┼éo.";
    }

    private static string Short(string text)
    {
        string clean = (text ?? "").Replace("\n", " ").Trim();
        return clean.Length <= 90 ? clean : clean[..90] + "ÔÇŽ";
    }
}
