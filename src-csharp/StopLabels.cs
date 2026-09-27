using System;
using System.Collections.Generic;

namespace Loupedeck.CodexDesktopPlugin;

internal static class StopLabels
{
    // Official values for chatgptConversations.composer.stop.ariaLabel in the
    // Codex Desktop locale bundles, plus legacy English accessibility labels.
    private static readonly HashSet<String> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stop",
        "Stop generating",
        "Stop response",
        "ለማቆም",
        "إيقاف",
        "Спиране",
        "বন্ধ করুন",
        "Zaustavi",
        "Atura",
        "Zastavit",
        "Stoppen",
        "Διακοπή",
        "Detener",
        "Peata",
        "توقف",
        "Pysäytä",
        "Arrêter",
        "બંધ કરો",
        "रोकें",
        "Leállítás",
        "Դադարեցնել",
        "Hentikan",
        "Stöðva",
        "Interrompi",
        "停止",
        "გაჩერება",
        "Тоқтату",
        "ನಿಲ್ಲಿಸಿ",
        "중지",
        "Sustabdyti",
        "Apturēt",
        "Запри",
        "നിർത്തുക",
        "Зогсоох",
        "थांबवा",
        "Berhenti",
        "ရပ်ရန်",
        "Stopp",
        "ਰੋਕੋ",
        "Zatrzymaj",
        "Parar",
        "Oprește",
        "Остановить",
        "Zastaviť",
        "Ustavi",
        "Jooji",
        "Ndalo",
        "Заустави",
        "Stoppa",
        "Sitisha",
        "நிறுத்தும்",
        "నిలిపివేయి",
        "หยุด",
        "Itigil",
        "Durdur",
        "Зупинити",
        "روکیں",
        "Ngừng",
    };

    public static Boolean IsStop(String value)
        => Labels.Contains(value.Trim());
}
