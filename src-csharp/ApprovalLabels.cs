using System;
using System.Collections.Generic;
using System.Linq;

namespace Loupedeck.CodexDesktopPlugin;

internal static class ApprovalLabels
{
    private static readonly HashSet<String> OneTimeLabels = BuildLabels("""
        Allow once
        Apply changes
        Allow network
        Approve
        Proceed
        Continue
        Atļaut vienreiz
        Autoriser une fois
        Benarkan sekali
        Bir kez izin ver
        Cho phép một lần
        Consenti una volta
        Dopusti jednom
        Dovoli enkrat
        Dozvoli jednom
        Eenmalig toestaan
        Egyszeri engedélyezés
        Einmal zulassen
        Hal mar oggolow
        Izinkan sekali
        Leisti vieną kartą
        Lejo një herë
        Leyfa einu sinni
        Luba üks kord
        Payagan nang isang beses
        Permet una vegada
        Permite o dată
        Permitir uma vez
        Permitir una vez
        Povolit jednou
        Povoliť raz
        Ruhusu mara moja
        Salli kerran
        Tillad én gang
        Tillat én gang
        Tillåt en gång
        Zezwól raz
        Να επιτραπεί μία φορά
        Бір рет рұқсат ету
        Дозволи еднаш
        Дозволи једном
        Дозволити один раз
        Нэг удаа зөвшөөрөх
        Разреши веднъж
        Разрешить один раз
        ერთხელ დაშვება
        Թույլատրել մեկ անգամ
        السماح مرة واحدة
        ایک بار اجازت دیں
        فقط این بار اجازه بده
        አንድ ጊዜ ፍቀድ
        एक बार अनुमति दें
        एकदाच अनुमती द्या
        একবার অনুমোদন করুন
        ਇੱਕ ਵਾਰ ਇਜਾਜ਼ਤ ਦਿਓ
        એકવાર મંજૂરી આપો
        ஒருமுறை அனுமதி
        ఒక్కసారి అనుమతించు
        ಒಮ್ಮೆ ಮಾತ್ರ ಅನುಮತಿಸಿ
        ഒരിക്കൽ മാത്രം അനുവദിക്കുക
        อนุญาตครั้งเดียว
        တစ်ကြိမ်သာ ခွင့်ပြုရန်
        한 번만 허용
        一度だけ許可
        允許一次
        允许一次
        """);

    private static readonly HashSet<String> PersistentLabels = BuildLabels("""
        Always allow
        Always approve
        Allow this conversation
        Allow for this conversation
        Allow for this session
        Approve for session
        Don't ask again
        Do not ask again
        Altijd toestaan
        Consenti sempre
        Her zaman izin ver
        Immer zulassen
        Lejo gjithmonë
        Leyfa alltaf
        Luba alati
        Luôn cho phép
        Mar walba oggolow
        Mindenkori engedélyezés
        Palaging payagan
        Permetre sempre
        Permite întotdeauna
        Permitir sempre
        Permitir siempre
        Ruhusu kila wakati
        Salli aina
        Selalu izinkan
        Sentiasa benarkan
        Tillad altid
        Tillat alltid
        Tillåt alltid
        Toujours autoriser
        Uvijek dopusti
        Uvijek dozvoli
        Vedno dovoli
        Vienmēr atļaut
        Visada leisti
        Vždy povolit
        Vždy povoliť
        Zawsze zezwalaj
        Να επιτρέπεται πάντα
        Әрқашан рұқсат ету
        Винаги разрешавай
        Всегда разрешать
        Завжди дозволяти
        Секогаш дозволувај
        Увек дозвољавај
        Үргэлж зөвшөөрөх
        ყოველთვის დაშვება
        Միշտ թույլատրել
        السماح دائمًا
        همیشه اجازه بده
        ہمیشہ اجازت دیں
        ሁልጊዜ ፍቀድ
        नेहमी अनुमती द्या
        हमेशा अनुमति दें
        সবসময় অনুমোদন করুন
        ਹਮੇਸ਼ਾ ਇਜਾਜ਼ਤ ਦਿਓ
        હંમેશાં મંજૂરી આપો
        எப்போதும் அனுமதி
        ఎల్లప్పుడూ అనుమతించు
        ಯಾವಾಗಲೂ ಅನುಮತಿಸಿ
        എപ്പോഴും അനുവദിക്കുക
        อนุญาตเสมอ
        အမြဲခွင့်ပြုရန်
        항상 허용
        一律允許
        始终允许
        常に許可
        Atļaut šo sarunu
        Autoriser cette conversation
        Benarkan perbualan ini
        Beszélgetés engedélyezése
        Bu konuşmaya izin ver
        Cho phép hội thoại này
        Consenti questa conversazione
        Dieses Gespräch zulassen
        Dopusti ovaj razgovor
        Dovoli ta pogovor
        Dozvoli ovaj razgovor
        In dit gesprek toestaan
        Izinkan percakapan ini
        Leisti šį pokalbį
        Lejo këtë bisedë
        Leyfa þessu samtali
        Luba see vestlus
        Oggolow wadahadalkan
        Payagan ang pag-uusap na ito
        Permet aquesta conversa
        Permite această conversație
        Permitir esta conversa
        Permitir esta conversación
        Povolit tuto konverzaci
        Povoliť túto konverzáciu
        Ruhusu mazungumzo haya
        Salli tämä keskustelu
        Tillad denne samtale
        Tillåt den här konversationen
        Tillat denne samtalen
        Zezwól na tę rozmowę
        Να επιτραπεί αυτή η συζήτηση
        Дозволи го овој разговор
        Дозволи овај разговор
        Дозволити цей чат
        Осы сөйлесуге рұқсат ету
        Позволете този разговор
        Разрешить этот разговор
        Энэ харилцан яриаг зөвшөөрөх
        ამ საუბრის დაშვება
        Թույլատրել այս խոսակցությունը
        اجازه دادن به این مکالمه
        اس گفتگو کی اجازت دیں
        السماح بهذه المحادثة
        ይህን ውይይት ፍቀድ
        इस बातचीत की अनुमति दें
        या संभाषणाला अनुमती द्या
        এই কথোপকথন অনুমোদন করুন
        ਇਸ ਗੱਲਬਾਤ ਦੀ ਇਜਾਜ਼ਤ ਦਿਓ
        આ વાતચીતને મંજૂરી આપો
        இந்த உரையாடலை அனுமதி
        ఈ సంభాషణను అనుమతించు
        ಈ ಸಂಭಾಷಣೆಯನ್ನು ಅನುಮತಿಸಿ
        ഈ സംഭാഷണം അനുവദിക്കുക
        อนุญาตการสนทนานี้
        ဤစကားပြောခန်းကို ခွင့်ပြုရန်
        이 대화 허용
        この会話を許可
        允許此對話
        允许此对话
        """);

    private static readonly HashSet<String> OptionsLabels = BuildLabels("""
        Approval options
        Permission options
        More approval options
        Alternativ för godkännande
        Apstiprināšanas opcijas
        Chaguo za uidhinishaji
        Genehmigungsoptionen
        Godkendelsesmuligheder
        Godkjenningsalternativer
        Goedkeuringsopties
        Hyväksyntäasetukset
        Jóváhagyási beállítások
        Kinnitamise valikud
        Mga opsyon sa pag-apruba
        Možnosti odobritve
        Možnosti schválení
        Možnosti schvaľovania
        Onay seçenekleri
        Opcije odobrenja
        Opciones de aprobación
        Opcions d’aprovació
        Opcje zatwierdzania
        Opções de aprovação
        Opsi persetujuan
        Opsionet e miratimit
        Options d’approbation
        Opțiuni de aprobare
        Opzioni di approvazione
        Patvirtinimo parinktys
        Pilihan kelulusan
        Tùy chọn phê duyệt
        Valkostir samþykkis
        Xulashooyinka ansixinta
        Επιλογές έγκρισης
        Зөвшөөрлийн сонголтууд
        Мақұлдау опциялары
        Опции за одобрение
        Опции за одобрување
        Опције одобравања
        Параметри схвалення
        Параметры утверждения
        დამტკიცების ვარიანტები
        Հաստատման տարբերակներ
        خيارات الموافقة
        گزینه‌های تأیید
        منظوری کے اختیارات
        የማጽደቅ አማራጮች
        अनुमोदन संबंधी विकल्प
        मंजुरीचे पर्याय
        অনুমোদনের বিকল্প
        ਮਨਜ਼ੂਰੀ ਦੇ ਵਿਕਲਪ
        મંજૂરીના વિકલ્પો
        ஒப்புதல் விருப்பங்கள்
        ఆమోద ఎంపికలు
        ಅನುಮೋದನೆ ಆಯ್ಕೆಗಳು
        അംഗീകാര ഓപ്ഷനുകൾ
        ตัวเลือกการอนุมัติ
        အတည်ပြုမှု ရွေးချယ်စရာများ
        승인 옵션
        审批选项
        審批選項
        承認オプション
        核准選項
        """);

    private static readonly HashSet<String> DenyLabels = BuildLabels("""
        Deny
        Reject
        Decline
        Don't allow
        Do not allow
        Ablehnen
        Afvis
        Atmesti
        Avslå
        Avvisa
        Denegar
        Diid
        Elutasítás
        Hafna
        Hylkää
        Kataa
        Keeldu
        Negar
        Noraidīt
        Odbij
        Odmietnuť
        Odmów
        Rebutja
        Rechazar
        Recusar
        Reddet
        Refuser
        Refuzo
        Respinge
        Rifiuta
        Tanggihan
        Tolak
        Từ chối
        Weigeren
        Zamítnout
        Zavrni
        Απόρριψη
        Бас тарту
        Відхилити
        Одбиј
        Отклонить
        Отхвърли
        Татгалзах
        უარყოფა
        Մերժել
        رد کردن
        رفض
        مسترد کریں
        ውድቅ አድርግ
        अस्वीकार करें
        नाकारा
        প্রত্যাখ্যান
        ਅਸਵੀਕਾਰ ਕਰੋ
        નકારો
        நிராகரி
        నిరాకరించు
        ನಿರಾಕರಿಸಿ
        നിരസിക്കുക
        ปฏิเสธ
        ငြင်းပယ်ရန်
        거부
        拒否
        拒絕
        拒绝
        """);

    private static readonly HashSet<String> StatusLabels = BuildLabels("""
        Awaiting approval
        Approval required
        Requires approval
        Permission required
        Needs approval
        Waiting for approval
        """);

    public static Boolean IsOneTime(String value) => Matches(value, OneTimeLabels);

    public static Boolean IsPersistent(String value) => Matches(value, PersistentLabels);

    public static Boolean IsOptions(String value) => Matches(value, OptionsLabels);

    public static Boolean IsDeny(String value) => Matches(value, DenyLabels);

    public static Boolean IsStatus(String value) => Matches(value, StatusLabels);

    private static Boolean Matches(String value, IReadOnlySet<String> labels)
    {
        var candidate = value.Trim();
        if (candidate.Length == 0)
        {
            return false;
        }

        if (labels.Contains(candidate))
        {
            return true;
        }

        return labels.Any(label =>
            candidate.StartsWith(label + " ", StringComparison.OrdinalIgnoreCase)
            || candidate.EndsWith(" " + label, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<String> BuildLabels(String values)
        => values
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
