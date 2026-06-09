# Bookero vs RentSpot - szczegółowa analiza porównawcza

Data analizy: 2026-05-07  
Analizowany konkurent: https://www.bookero.pl  
Analizowany projekt: RentSpot / repo `SportRentalHybrid`  
Zakres: funkcje produktu, UX, model biznesowy, integracje, stan techniczny, ryzyka i rekomendacje.

## 1. Wniosek wykonawczy

Bookero i RentSpot nie są bezpośrednio tym samym typem systemu.

Bookero jest szerokim, horyzontalnym systemem rezerwacji online: terminarz, formularze, widget na stronę, płatności, SMS-y, panel klienta, integracje i gotowy cennik SaaS. Sprzedaje przede wszystkim prostą obietnicę: klient rezerwuje sam, firma mniej odbiera telefonów, a kalendarz i komunikacja same się porządkują.

RentSpot jest bardziej pionowym systemem operacyjnym dla wypożyczalni sprzętu sportowego. Ma katalog sprzętu, koszyk, płatności Stripe, depozyty, obsługę wydań i zwrotów, umowy PDF, statusy sprzętu, QR/kody kreskowe, skaner, scoring zaufania klienta, opinie, raporty i panel administracyjny dla realnej pracy wypożyczalni.

Najkrócej:

- Bookero wygrywa produktem SaaS jako opakowaniem: cennik, trial, widgety, integracje, panel klienta, karnety/vouchery, marketing i niski próg wejścia.
- RentSpot wygrywa głębokością operacyjną dla wypożyczalni sprzętu: inwentarz, wydanie/zwrot, depozyt, umowa, szkody, kody, mapa, logika rentalowa.
- Najlepsza strategia dla RentSpot to nie kopiować Bookero jako ogólny terminarz, tylko wziąć jego najlepsze elementy samoobsługi i opakowania SaaS, a pozycjonować RentSpot jako specjalistyczny system dla wypożyczalni sprzętu sportowego.

## 2. Źródła i metoda

### Źródła Bookero

Analiza Bookero została wykonana na podstawie publicznie dostępnych stron:

- Strona główna: https://www.bookero.pl/
- Cennik: https://www.bookero.pl/cennik
- Lista funkcji: https://www.bookero.pl/funkcje
- Panel klienta: https://www.bookero.pl/funkcje/panel-klienta
- Integracje: https://www.bookero.pl/integracje
- Moduł karnetów: https://www.bookero.pl/news/modul-karnetow-w-systemie-rezerwacji-online
- Przykłady wdrożeń: https://www.bookero.pl/przyklady
- Przykładowa publiczna strona rezerwacji: https://treningi.bookero.pl/

Nie analizowałem zamkniętego panelu administracyjnego Bookero po zalogowaniu ani płatnych funkcji od środka. Wnioski o UX i funkcjach Bookero opierają się na publicznych materiałach, cenniku, stronie funkcji, integracji i publicznym demo.

### Źródła RentSpot

Analiza RentSpot została wykonana przez odświeżenie bieżącego repozytorium:

- `README.md`
- `SportRental.Admin/Api/Endpoints.cs`
- `SportRental.Admin/Payments/CheckoutFinalizationService.cs`
- `SportRental.Admin/Components/Pages/Admin/*.razor`
- `SportRental.Client/Pages/*.razor`
- `SportRental.Infrastructure/Domain/*.cs`
- `SportRental.Shared/Models/*.cs`
- `.github/workflows/ci.yml`
- ostatnie commity, w tym rebrand SportRental -> RentSpot oraz poprawki IDOR / webhook Stripe.

W repo nazwy projektów i namespace'y nadal są `SportRental.*`, natomiast warstwa UI jest częściowo przebrandowana na `RentSpot`.

## 3. Profil Bookero

Bookero to gotowy system rezerwacji online dla wielu branż usługowych. Z publicznych materiałów wynika, że obejmuje m.in.:

- rezerwacje terminów online 24/7,
- kalendarz wizyt/rezerwacji,
- zarządzanie rezerwacjami,
- przypomnienia SMS,
- integrację z Google Calendar,
- bazę klientów / CRM,
- statystyki,
- panel klienta,
- płatności online BookeroPay oparte na Stripe,
- widget rezerwacji na stronę,
- integrację z WordPressem,
- logowanie/rejestrację przez Google i Facebooka,
- kody QR dla wizyt/rezerwacji,
- własną domenę w wyższych planach,
- kody zniżkowe, promocje, grupy klientów,
- vouchery, pakiety usług, karnety i wejściówki,
- API/skrypty JS dla bardziej zaawansowanych integracji.

Bookero obsługuje różne piony biznesowe: wizyty, zajęcia, bilety, wypożyczalnie, wynajem obiektów i centra usługowe. W przykładach publicznych ma też przypadki typu wypożyczalnia sprzętu sportowego, korty tenisowe, escape roomy, sale konferencyjne, gokarty, wycieczki i zajęcia.

Najważniejsze cechy komercyjne Bookero:

- 14-dniowy test bez podawania płatności.
- Plan Standard od 20 PLN/mies. dla 1 zasobu i 1 administratora.
- Plan Plus 80 PLN/mies. dla 5 zasobów i 5 administratorów.
- Plan Premium 200 PLN/mies. dla 20 zasobów i 20 administratorów.
- Plan Enterprise wyceniany indywidualnie.
- SMS-y w limitach pakietowych oraz dodatkowo płatne paczki SMS.
- Płatności BookeroPay z opłatą 1,4% + 1 PLN według publicznego cennika.

Bookero jest bardzo dobrze "opakowane" jako samoobsługowy produkt SaaS: użytkownik rozumie, co kupuje, ile płaci, jak zaczyna i jakie ma rozszerzenia.

## 4. Profil RentSpot

RentSpot to specjalistyczny system dla wypożyczalni sprzętu sportowego. Obecnie składa się z:

- `SportRental.Admin` - panel administracyjny Blazor Server oraz API dla klienta WASM,
- `SportRental.Client` - publiczna aplikacja Blazor WebAssembly,
- `SportRental.Infrastructure` - EF Core, domena, migracje,
- `SportRental.Shared` - DTO, modele, usługi współdzielone,
- `SportRental.MediaStorage` - obecnie obecny w solution, ale dokumentacja opisuje go jako nieaktywny/nieużywany w głównym flow,
- testów jednostkowych/integracyjnych oraz CI.

Najmocniejsze funkcje produktu:

- publiczny katalog sprzętu,
- filtrowanie produktów po kategorii, mieście, województwie, dostępności, cenie i odległości,
- mapa wypożyczalni i lokalizacji,
- koszyk,
- checkout,
- Stripe Checkout,
- depozyty,
- quote płatności,
- finalizacja po Stripe webhook i fallback po powrocie klienta ze Stripe,
- historia wypożyczeń klienta,
- szczegóły wypożyczenia,
- pobieranie umów,
- opinie klientów po wynajmie,
- panel właściciela/pracownika,
- produkty z ilością, SKU, numerem seryjnym, zdjęciami, QR/kodami kreskowymi,
- etykiety kodów,
- skaner QR/kodów w panelu i WASM,
- wynajmy online i stacjonarne,
- wydanie sprzętu,
- zwrot sprzętu,
- zwrot depozytu,
- opłaty za szkody,
- SMS/email tracking,
- przypomnienia,
- kalendarz rezerwacji,
- raporty,
- scoring zaufania klienta,
- role: SuperAdmin, Owner, Employee, Client,
- multi-tenant,
- branding tenanta,
- kontrakty PDF QuestPDF,
- Azure Blob / App_Data / S3 / storage abstraction,
- asystent AI dla panelu administracyjnego z narzędziami odczytu/zapisu.

Ostatnie zmiany w repo są istotne:

- commit `a6e1b77` przebrandował UI SportRental -> RentSpot, zmienił motyw, favicon i PDF,
- commit `c1af6de` dodał poprawki bezpieczeństwa IDOR, webhook Stripe, sprzątanie CI/Docker,
- `CheckoutFinalizationService` jest teraz wspólną, idempotentną ścieżką finalizacji dla webhooka Stripe i redirect fallback.

## 5. Największa różnica strategiczna

Bookero jest systemem "rezerwuję termin/usługę". RentSpot jest systemem "wypożyczam i obsługuję fizyczny sprzęt".

To bardzo ważne, bo w wypożyczalni sprzętu sama rezerwacja to tylko część problemu. Prawdziwe procesy operacyjne to:

- czy sprzęt jest realnie dostępny,
- ile sztuk jest dostępnych,
- czy sprzęt jest wydany,
- komu jest wydany,
- kiedy ma wrócić,
- czy wrócił na czas,
- czy wrócił uszkodzony,
- ile depozytu zwrócić,
- czy klient miał incydenty,
- czy trzeba wygenerować umowę,
- czy pracownik ma szybko zeskanować kod,
- czy sprzęt ma etykietę,
- czy sezonowo konkretny model jest najbardziej obciążony.

RentSpot rozwiązuje więcej z tych operacyjnych problemów niż Bookero pokazuje publicznie. Bookero za to dużo lepiej rozwiązuje wejście klienta do rezerwacji, integracje z otoczeniem firmy i sprzedaż SaaS.

## 6. Porównanie funkcji

| Obszar | Bookero | RentSpot | Ocena |
|---|---|---|---|
| Publiczna rezerwacja online | Bardzo mocna, centralna funkcja produktu | Jest katalog, koszyk i checkout | Bookero ma dojrzalszy booking-first UX |
| Katalog sprzętu | Wspiera scenariusze wypożyczalni, ale publicznie wygląda bardziej generycznie | Szczegółowy katalog sprzętu z SKU, ilością, ceną dzienną/godzinową, zdjęciami, lokalizacją | RentSpot mocniejszy domenowo |
| Terminarz/kalendarz | Główna funkcja, mocny nacisk na sloty i wizyty | Panel `Schedule` pokazuje rezerwacje w czasie | Bookero mocniejsze jako terminarz, RentSpot jako kalendarz operacji |
| Zarządzanie dostępnością | Zasoby/usługi/sloty | Ilość sprzętu, wynajmy, statusy, daty, dostępność | Remis zależny od branży; RentSpot lepszy dla fizycznego inwentarza |
| Wydanie i zwrot sprzętu | Brak mocnego publicznego sygnału | Model i UI obsługują wydanie, zwrot, notatki, zwrot depozytu, szkody | Duża przewaga RentSpot |
| Depozyty | Brak wyraźnego publicznego nacisku | Depozyt jest częścią modelu płatności i wynajmu | Przewaga RentSpot |
| Umowy PDF | Nie widać jako kluczowej funkcji publicznie | QuestPDF, generowanie i wysyłka umowy | Przewaga RentSpot |
| Płatności online | BookeroPay / Stripe, wiele metod płatności, jasny cennik prowizji | Stripe Checkout, webhook, finalizacja, depozyt | Bookero lepsze opakowanie, RentSpot lepsze dopasowanie do rentalu |
| SMS | Limity SMS w planach, przypomnienia | SerwerSMS/SMSAPI, tracking potwierdzeń i przypomnień | Remis funkcjonalny, Bookero lepsze pakietowanie |
| Panel klienta | Wyraźnie opisany: historia, anulowanie, edycja, płatność, formularze | Konto, profil, moje wypożyczenia, opinie, trust summary | Bookero dojrzalsze w self-service, RentSpot ma dobry fundament |
| Opinie | Feedback klientów jako funkcja rozwoju biznesu | RentalReview, item review, public/review flow, review request service | RentSpot ma bardziej domenowy system opinii |
| Scoring klienta | Brak widocznej analogicznej funkcji | CustomerTrustLevel, agregat cross-tenant, override admina | Duża przewaga RentSpot |
| Karnety/pakiety/vouchery | Silna funkcja publicznie opisana | Brak widocznego modułu karnetów/voucherów | Duża przewaga Bookero |
| Promocje i kupony | Dostępne w planie Plus/Premium | Brak wyraźnego modułu kuponów | Przewaga Bookero |
| Widget na stronę | Jedna z głównych funkcji, plus WordPress | Brak gotowego embeddable widgetu | Duża przewaga Bookero |
| API/skrypty JS | Publicznie komunikowane | API istnieje wewnętrznie dla WASM, brak produktu developerskiego | Bookero lepsze komercyjnie |
| Logowanie Google/Facebook | Tak | Email/cookie/JWT; brak widocznego social loginu w kliencie publicznym | Przewaga Bookero |
| Google Calendar | Tak | Brak widocznej integracji | Przewaga Bookero |
| QR | QR dla wizyt/rezerwacji | QR/kody kreskowe dla sprzętu, etykiety, skaner | RentSpot lepszy dla sprzętu |
| Własna domena/mikrostrona | Bookero oferuje w wyższych planach | Brak widocznego produktu "własna strona rezerwacji" | Przewaga Bookero |
| Multi-tenant | Tak jako SaaS | Tak w architekturze i domenie | Remis techniczny |
| Raporty/statystyki | Statystyki jako funkcja | Raporty przychodów, top produktów, top klientów, statusy, utilization | RentSpot wygląda mocniej operacyjnie |
| AI/asystent admina | Nie widać w publicznych materiałach | Floating chat z narzędziami operacyjnymi | Przewaga RentSpot |
| Cennik i trial | Bardzo jasne | Brak gotowego publicznego cennika/trial flow w repo | Duża przewaga Bookero |

## 7. UX i ścieżka użytkownika

### Bookero

Bookero prowadzi użytkownika bardzo prosto:

1. Firma zakłada konto.
2. Dostaje okres testowy.
3. Konfiguruje usługi/zasoby/kalendarz.
4. Wstawia link/widget na stronę albo używa publicznej strony Bookero.
5. Klient rezerwuje termin.
6. System wysyła potwierdzenia/przypomnienia, obsługuje płatność i panel klienta.

W publicznym demo `treningi.bookero.pl` widać prosty flow wyboru kategorii, usługi, ceny, czasu trwania i przycisku wyboru. To jest mało skomplikowane, ale dobrze działa dla usług.

### RentSpot

RentSpot ma bardziej rozbudowany flow:

1. Klient wybiera wypożyczalnię/tenant.
2. Przegląda katalog sprzętu.
3. Filtruje po kategorii, lokalizacji, dostępności, cenie, odległości.
4. Dodaje sprzęt do koszyka.
5. Wybiera daty i dane kontaktowe.
6. Płaci przez Stripe.
7. System tworzy wynajem po webhooku/fallbacku.
8. Klient ma historię wynajmów i umowę.
9. Wypożyczalnia wydaje sprzęt, śledzi zwrot, depozyt, szkody.
10. Po zakończeniu może zbierać opinię i aktualizować scoring.

To jest cięższy flow niż Bookero, ale odpowiada na bardziej złożony problem. RentSpot powinien jednak uprościć pierwszy kontakt klienta, bo Bookero ma przewagę w szybkości wejścia w rezerwację.

Największe braki UX RentSpot względem Bookero:

- brak gotowego embeddable widgetu,
- brak krótkiego publicznego formularza "zarezerwuj usługę/zasób" dla strony wypożyczalni,
- brak samoobsługowego trialu dla właściciela,
- brak jasnej strony cennika,
- brak konfiguratora formularza rezerwacji,
- brak Google Calendar jako standardowej integracji,
- brak gotowego "anuluj/przełóż rezerwację" w panelu klienta na poziomie takim, jaki Bookero komunikuje publicznie.

## 8. Model biznesowy i pakiety

### Bookero

Bookero ma czytelny SaaS:

- Standard: 20 PLN/mies., 1 zasób, 1 administrator, 10 SMS/mies.
- Plus: 80 PLN/mies., 5 zasobów, 5 administratorów, 100 SMS/mies.
- Premium: 200 PLN/mies., 20 zasobów, 20 administratorów, 300 SMS/mies.
- Enterprise: indywidualnie.
- Dodatkowe SMS-y jako paczki.
- BookeroPay: 1,4% + 1 PLN według cennika.

To jest niska bariera wejścia. Mała firma może zacząć tanio, a większa dopłaca za zasoby, administratorów, SMS-y, domenę, promocje, grupy klientów i premium powiadomienia.

### RentSpot

W repo RentSpot ma dokumenty wyceny/licencjonowania, ale nie ma tak jednoznacznego publicznego produktu SaaS jak Bookero. Z perspektywy klienta biznesowego brakuje odpowiedzi:

- Ile zapłacę miesięcznie?
- Czy mam trial?
- Ile sprzętu mogę dodać?
- Ile wypożyczalni/lokalizacji obsłużę?
- Ile pracowników mogę dodać?
- Ile SMS-ów w pakiecie?
- Czy płacę prowizję od Stripe?
- Czy branding/własna domena są w cenie?
- Czy umowy PDF i skaner są w każdym planie?

Proponowany kierunek pakietów dla RentSpot:

| Plan | Cena orientacyjna | Limit | Dla kogo |
|---|---:|---|---|
| Starter | 49-79 PLN/mies. | 1 lokalizacja, 50 produktów, 2 użytkowników, podstawowy checkout | małe wypożyczalnie sezonowe |
| Pro | 149-249 PLN/mies. | 3 lokalizacje, 500 produktów, 10 użytkowników, SMS, PDF, kody, raporty | typowa wypożyczalnia sportowa |
| Business | 399-699 PLN/mies. | wiele lokalizacji, zaawansowane raporty, scoring, AI, integracje | sieci wypożyczalni |
| Enterprise | indywidualnie | SLA, migracje, custom integracje, własna infrastruktura | duże sieci / white label |

RentSpot nie powinien konkurować ceną 1:1 ze Standardem Bookero za 20 PLN, bo rozwiązuje cięższy problem operacyjny. Wejściowy plan może być droższy, jeżeli komunikacja jasno pokazuje: inwentarz, depozyty, umowy, zwroty, szkody, kody i raporty.

## 9. Integracje

Bookero ma przewagę w gotowych integracjach front-office:

- Google Calendar,
- Google login,
- Facebook login,
- karta na Facebooku,
- WordPress,
- widget na stronę,
- QR dla wizyt,
- Vimeo/YouTube dla zajęć online,
- BookeroPay/Stripe,
- API/skrypty JS.

RentSpot ma przewagę w integracjach operacyjno-technicznych:

- Stripe Checkout,
- Stripe webhook,
- SMSAPI/SerwerSMS,
- Azure Key Vault,
- Azure Blob Storage,
- S3-compatible storage,
- QR/kody kreskowe,
- Leaflet map,
- QuestPDF,
- OpenAI/Azure OpenAI dla asystenta,
- SignalR dla powiadomień o statusie wynajmu.

Rekomendacja: dla RentSpot priorytetem nie powinno być kopiowanie wszystkich integracji Bookero. Najpierw warto dodać te, które skracają sprzedaż:

1. widget rezerwacji na stronę,
2. Google Calendar sync dla właściciela,
3. WordPress plugin / prosty embed,
4. własna publiczna strona wypożyczalni pod subdomeną,
5. kupony/promocje,
6. pakiety/karnety dla sportów sezonowych.

## 10. Bezpieczeństwo i dojrzałość techniczna

### Mocne strony RentSpot

Ostatnie zmiany poprawiają realne ryzyka:

- endpointy z kontraktami, logo i zdjęciami mają dodatkowe kontrole tenant/rola,
- webhook Stripe weryfikuje podpis,
- finalizacja checkoutu jest idempotentna,
- fallback po redirect ze Stripe nie jest już jedyną ścieżką tworzenia wynajmu,
- CI filtruje testy wymagające żywej bazy, zewnętrznych usług, WebFactory i Dockera,
- są testy security na anonimowe żądania i cross-tenant access.

To jest bardzo dobra zmiana jakościowa. Szczególnie webhook Stripe jest kluczowy: bez niego klient mógł zapłacić, zamknąć kartę i nie wrócić do aplikacji, a wynajem mógł nigdy nie powstać.

### Ryzyka i dług techniczny RentSpot

1. Rebrand nie jest domknięty.

   UI i motyw mówią `RentSpot`, ale `README.md`, część dokumentacji i niektóre maile nadal używają `SportRental`. Szybki pomiar w repo: 237 plików zawiera `SportRental`, a 44 pliki zawierają `RentSpot`. Część `SportRental` jest poprawna technicznie jako namespace/projekt, ale materiały publiczne i maile powinny zostać wyczyszczone.

2. README jest marketingowo i technicznie przestarzałe.

   Nadal pokazuje `SportRental`, stare liczby testów i fragmenty architektury, które nie odzwierciedlają w pełni obecnego stanu po cleanupie CI/Docker i rebrandzie.

3. Build przechodzi, ale z ostrzeżeniami.

   `dotnet build SportRentalHybrid.sln -c Debug --no-restore` zakończył się sukcesem, ale z 95 ostrzeżeniami. Najważniejsze grupy:

   - preview SDK .NET 10,
   - podatności NuGet: `AWSSDK.Core`, `MailKit`, `MimeKit`, `RestSharp`,
   - nullable warnings,
   - ostrzeżenia MudBlazor/Blazor analyzer,
   - `EF1002` w testach,
   - ostrzeżenia xUnit.

4. Brak `global.json`.

   Repo targetuje `net10.0` i używa preview SDK, ale nie ma widocznego `global.json` pinującego wersję SDK. To zwiększa ryzyko różnic między lokalnym środowiskiem, CI i maszynami deweloperów.

5. `POST /api/customers` jest publiczny.

   Jest to uzasadnione jako wejście dla checkoutu/guest flow, ale duplicate email zwraca `409`, więc przy znanym tenant context może ujawniać, czy email już istnieje. To nie musi blokować produktu, ale warto rozważyć mniej informacyjny flow albo rate limit/specjalną ścieżkę guest-session.

6. Część dokumentacji opisuje stare hostingi i projekty.

   `SportRental.Api`, `MediaStorage`, CORS dla starych adresów i dokumenty deploymentowe wymagają przeglądu, żeby onboarding deweloperski nie prowadził w stare ścieżki.

## 11. Co Bookero robi lepiej

### 11.1. Jasny produkt do kupienia

Bookero ma prosty cennik i trial. To mocno zmniejsza tarcie sprzedażowe. RentSpot ma funkcje, ale nie ma równie jasnej publicznej oferty.

Rekomendacja dla RentSpot:

- stworzyć stronę `/pricing` albo dokument ofertowy,
- dodać 14-dniowy trial dla Ownera,
- zdefiniować limity planów,
- jasno pokazać, co jest w planie, a co jest dodatkiem.

### 11.2. Widget i osadzanie na stronie

Bookero pozwala użyć linku, kalendarza do osadzenia albo przycisku rezerwacji. To jest bardzo ważne, bo większość małych firm ma już stronę/Facebook/Google Business Profile i chce tylko dodać rezerwacje.

Rekomendacja dla RentSpot:

- publiczny widget JS `RentSpot.BookingWidget`,
- iframe/embed katalogu wybranego tenanta,
- tryb "produkt + data + dane + płatność" bez pełnego przechodzenia przez aplikację,
- WordPress shortcode/plugin jako druga faza.

### 11.3. Pakiety, karnety, vouchery

Bookero ma mocny moduł karnetów/pakietów. Dla sportu to bardzo ważne:

- wypożyczenie 5 wejść na SUP,
- sezonowy karnet rowerowy,
- pakiet weekendowy,
- voucher prezentowy,
- zajęcia + sprzęt,
- karta lojalnościowa dla klientów powracających.

Rekomendacja dla RentSpot:

- model `Voucher` / `RentalPackage` / `Pass`,
- saldo wejść lub saldo PLN,
- data ważności,
- przypisanie do klienta,
- możliwość płatności online,
- wykorzystanie przy checkout.

### 11.4. Integracje kalendarzowe i social login

Google Calendar i logowanie Google/Facebook to dla małych usługodawców bardzo praktyczne funkcje. RentSpot może działać bez nich, ale w sprzedaży porównawczej Bookero będzie wyglądało bardziej kompletne.

Rekomendacja:

- Google Calendar sync dla rezerwacji Ownera,
- "Add to calendar" dla klienta,
- Google login jako pierwszy social login,
- Facebook login tylko jeśli realni klienci tego potrzebują.

## 12. Co RentSpot robi lepiej

### 12.1. Fizyczny inwentarz

RentSpot ma model sprzętu, SKU, numer seryjny, ilość, zdjęcia, lokalizację, kody, stan dostępności. Bookero może obsługiwać zasoby, ale publicznie nie pokazuje takiego nacisku na cykl życia fizycznego sprzętu.

To jest kluczowa przewaga, którą trzeba mocniej komunikować.

### 12.2. Wydanie, zwrot, depozyt, szkody

RentSpot ma pola i UI pod:

- `IssuedAtUtc`,
- `ReturnedAtUtc`,
- `IssueNotes`,
- `ReturnNotes`,
- `ReturnDepositRefund`,
- `DamageCharge`.

To są funkcje, które mają realną wartość dla wypożyczalni. Bookero wygląda bardziej jak system rezerwacji i sprzedaży terminu, a nie system obsługi zwrotu sprzętu.

### 12.3. Umowy PDF

Automatyczne umowy PDF z danymi firmy i wynajmu są dużym wyróżnikiem. Dla sprzętu sportowego, depozytów i szkód to może być argument sprzedażowy ważniejszy niż kolejna integracja social.

### 12.4. QR/kody kreskowe jako etykiety sprzętu

Bookero używa QR jako elementu wizyty/rezerwacji. RentSpot używa kodów bliżej fizycznego sprzętu: etykiety, skaner, identyfikacja produktu. To jest bardziej wartościowe dla wypożyczalni.

### 12.5. Trust scoring klienta

Customer trust agregowany z historii ocen wypożyczalni to mocna, unikalna funkcja. Trzeba ją jednak opisywać ostrożnie prawnie i produktowo:

- pokazywać agregat, nie komentarze,
- unikać ujawniania źródła opinii między tenantami,
- dać klientowi jasną ścieżkę wyjaśnienia/sporu, jeśli status ogranicza usługę,
- opisać politykę retencji i podstawę przetwarzania.

### 12.6. Asystent AI dla operacji

Bookero publicznie nie komunikuje asystenta operacyjnego. RentSpot ma floating chat z narzędziami do:

- dzisiejszych wynajmów,
- statusu produktu,
- trust klienta,
- aktywnych wynajmów,
- zaległości,
- przychodów,
- top klientów,
- forecast demand,
- aktualizacji notatek,
- oznaczania zwrotu,
- wysyłania SMS.

To jest wartościowe, ale powinno być sprzedawane dopiero po ustabilizowaniu podstawowego SaaS flow. Dla nowych klientów najpierw liczy się: rezerwacja, sprzęt, płatność, umowa, SMS.

## 13. Rekomendowany plan produktu

### Priorytet P0 - domknąć fundament komercyjny

1. Ujednolicić markę RentSpot.

   - Publiczne UI, maile, PDF, favicon, README, instrukcje, dokumenty ofertowe.
   - Zostawić namespace `SportRental` tylko tam, gdzie zmiana byłaby kosztowna technicznie.

2. Naprawić ostrzeżenia bezpieczeństwa NuGet.

   - Zaktualizować `AWSSDK.S3` / transitive `AWSSDK.Core`.
   - Zaktualizować `MailKit` i `MimeKit`.
   - Sprawdzić `SMSAPI.pl` lub inne zależności wprowadzające `RestSharp`.

3. Dodać `global.json`.

   - Pin preview SDK używany lokalnie/CI albo przejść na stabilny zestaw SDK, jeśli dostępny.

4. Urealnić README i dokumentację.

   - Aktualna architektura.
   - Aktualna nazwa RentSpot.
   - Aktualne komendy build/test.
   - Aktualny deployment.
   - Aktualny status projektów `Api`/`MediaStorage`.

5. Zdefiniować cennik.

   - Nawet jeśli tymczasowy, potrzebny jest punkt odniesienia dla rozmów z klientami.

### Priorytet P1 - dogonić Bookero w samoobsłudze

1. Publiczna strona rezerwacji dla tenanta.

   - URL typu `/r/{tenantSlug}` albo subdomena.
   - Katalog + daty + checkout + kontakt.
   - Minimalna wersja bez pełnej personalizacji.

2. Widget rezerwacji.

   - Link.
   - Iframe.
   - JS embed.
   - Przycisk "Zarezerwuj sprzęt".

3. Panel klienta: anulowanie i zmiana rezerwacji.

   - Zasady per tenant.
   - Cutoff czasowy.
   - Opcjonalna dopłata/zwrot.

4. Kupony i promocje.

   - Kod procentowy/kwotowy.
   - Ważność.
   - Limit użyć.
   - Przypisanie do tenanta.

5. Google Calendar.

   - Synchronizacja rezerwacji ownera.
   - ICS dla klienta jako prostszy etap pośredni.

### Priorytet P2 - rozszerzenia revenue

1. Karnety/pakiety/vouchery.

   - Szczególnie dla sportów sezonowych.
   - Dobra monetyzacja i lojalność.

2. Własna domena / branding strony tenanta.

   - W wyższych planach.
   - Prosty kreator: logo, kolor, opis, zdjęcie hero, regulamin.

3. Zaawansowane raporty sezonowe.

   - Utilization per sprzęt.
   - Forecast demand.
   - Marżowość kategorii.
   - Szkody/depozyty.

4. Integracje marketingowe.

   - Google Business Profile link.
   - Meta/Facebook link.
   - UTM tracking dla widgetu.

## 14. Rekomendowane pozycjonowanie

Nie rekomenduję pozycjonowania RentSpot jako "alternatywy dla Bookero" w sensie ogólnego systemu rezerwacji. To skazuje produkt na walkę z dojrzałym, szerokim narzędziem, które ma wiele integracji i niski cennik.

Lepsze pozycjonowanie:

> RentSpot to system rezerwacji i obsługi wypożyczalni sprzętu sportowego: katalog, płatności, depozyty, umowy, wydanie, zwrot, kody sprzętu i raporty.

Wariant krótszy:

> Bookero rezerwuje termin. RentSpot obsługuje cały cykl wypożyczenia sprzętu.

To rozróżnienie jest bardzo mocne sprzedażowo.

## 15. Lista braków względem Bookero

Najważniejsze luki do zamknięcia, jeśli klient porówna oba systemy:

1. Brak publicznego cennika SaaS.
2. Brak trialu 14 dni / samoobsługowego onboardingu.
3. Brak widgetu na stronę.
4. Brak WordPress/plugin/embed.
5. Brak Google Calendar sync.
6. Brak karnetów, voucherów i pakietów.
7. Brak kuponów/promocji.
8. Brak własnej domeny jako produktu.
9. Brak social loginu.
10. Brak publicznej strony "funkcje" z klarowną segmentacją.
11. Brak czytelnego flow dla firm usługowych innych niż wypożyczalnie.
12. Niedomknięty rebrand i dokumentacja.

## 16. Lista przewag względem Bookero

Najważniejsze atuty RentSpot, które należy eksponować:

1. Specjalizacja w sprzęcie sportowym.
2. Realny inwentarz z ilościami i lokalizacją.
3. SKU, numery seryjne, kody QR/kreskowe.
4. Skaner i etykiety sprzętu.
5. Wydanie i zwrot sprzętu.
6. Depozyty i szkody.
7. Umowy PDF.
8. Historia klienta i scoring zaufania.
9. Mapa wypożyczalni.
10. Raporty wykorzystania sprzętu.
11. AI assistant dla admina.
12. Głębsza kontrola tenant/security w modelu.

## 17. Ryzyka konkurencyjne

1. Bookero może być "wystarczająco dobre".

   Mała wypożyczalnia może zaakceptować prostszy system, jeśli kosztuje 20-80 PLN/mies. i łatwo go osadzić na stronie. RentSpot musi pokazać, za co bierze wyższą cenę.

2. Bookero ma mniejszy próg wejścia.

   Klient może założyć konto i testować bez rozmowy handlowej. RentSpot potrzebuje podobnej ścieżki albo bardzo sprawnego procesu demo.

3. Bookero ma mocniejszą wiarygodność marketingową.

   Publiczne przykłady, cennik, funkcje, branże i integracje budują zaufanie. RentSpot ma dużo w kodzie, ale musi to pokazać na zewnątrz.

4. RentSpot może wyglądać zbyt ciężko.

   Jeśli pierwszy ekran pokaże za dużo pojęć operacyjnych, mała firma może wybrać Bookero. Dlatego publiczny booking i onboarding powinny być proste, a zaawansowane operacje widoczne dopiero po wejściu w panel.

## 18. Ryzyka techniczne przed sprzedażą

1. Ostrzeżenia NuGet o podatnościach trzeba zamknąć przed poważniejszą sprzedażą.
2. Trzeba uruchamiać CI na stabilnym, spójnym SDK.
3. Dokumentacja musi przestać mieszać SportRental/RentSpot.
4. Publiczne endpointy guest/customer wymagają dalszego przeglądu pod enumerację i rate limiting.
5. Stare ścieżki deploymentowe mogą powodować błędy przy wdrożeniu przez inną osobę.
6. Trzeba oddzielić opis "co działa produkcyjnie" od "co jest w repo, ale nieużywane".

## 19. Proponowany backlog porównawczy

| Priorytet | Zadanie | Dlaczego |
|---|---|---|
| P0 | Rebrand cleanup dokumentów/maili | Obecna niespójność osłabia wiarygodność |
| P0 | Update podatnych paczek | Bezpieczeństwo i sprzedaż B2B |
| P0 | `global.json` i aktualizacja README | Powtarzalność buildów |
| P0 | Publiczny cennik RentSpot | Bookero ma jasny cennik, RentSpot nie |
| P1 | Publiczna strona rezerwacji tenanta | Podstawowy element SaaS booking |
| P1 | Widget iframe/JS | Największa luka wobec Bookero |
| P1 | Kupony/promocje | Szybka funkcja sprzedażowa |
| P1 | Google Calendar/ICS | Oczekiwana integracja terminarza |
| P2 | Karnety/vouchery | Duża luka funkcjonalna i revenue |
| P2 | Własna domena/branding | Funkcja planu premium |
| P2 | WordPress plugin | Ułatwia adopcję małym firmom |
| P2 | Landing "RentSpot dla wypożyczalni" | Trzeba pokazać przewagi nad Bookero |

## 20. Stan weryfikacji lokalnej

Wykonane:

- `git log -5 --oneline --decorate` - potwierdzono ostatnie zmiany, w tym rebrand i poprawki security/Stripe.
- `dotnet sln SportRentalHybrid.sln list` - potwierdzono aktualne projekty w solution.
- `dotnet build SportRentalHybrid.sln -c Debug --no-restore` - build zakończony sukcesem: 0 błędów, 95 ostrzeżeń.
- `dotnet test SportRentalHybrid.sln -c Debug --no-build --filter "Category!=RequiresLiveDb&Category!=RequiresLiveServices&Category!=RequiresWebFactory&Category!=RequiresDocker&Category!=Integration"` - sukces: 400/400 testów przeszło. Rozbicie: `SportRental.Admin.Tests` 388/388, `SportRental.Client.Tests` 6/6, `SportRental.MediaStorage.Tests` 6/6.

Najważniejsze ostrzeżenia z buildu:

- .NET 10 preview SDK (`NETSDK1057`),
- podatne zależności NuGet: `AWSSDK.Core`, `MailKit`, `MimeKit`, `RestSharp`,
- nullable warnings,
- ostrzeżenia analyzerów Blazor/MudBlazor,
- ostrzeżenia xUnit,
- `EF1002` w test fixture.

## 21. Konkluzja

Bookero jest silnym wzorcem dla opakowania SaaS: cennik, trial, widgety, integracje, panel klienta, karnety, płatności i marketing. RentSpot nie powinien ignorować tych elementów, bo klient porównujący systemy będzie je widział natychmiast.

Jednocześnie RentSpot ma realnie głębszy produkt dla wypożyczalni sprzętu sportowego. Największa szansa jest w specjalizacji: obsłużyć cały cykl życia wypożyczenia fizycznego sprzętu, a nie tylko rezerwację terminu.

Najlepszy kierunek:

1. Domknąć rebrand i techniczne ostrzeżenia.
2. Dodać cennik/trial/widget/publiczną stronę rezerwacji.
3. Zachować mocną specjalizację: sprzęt, depozyty, umowy, wydanie, zwrot, szkody, kody i raporty.
4. Dopiero potem rozwijać karnety, vouchery, domeny i integracje premium.

Jeżeli RentSpot przejmie od Bookero łatwość startu, ale utrzyma przewagę operacyjną dla sprzętu, będzie miał dużo mocniejsze pozycjonowanie niż jako kolejny ogólny terminarz online.
