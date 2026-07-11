# Client WASM — bezpieczne testy read-only

Pakiet `ClientReadOnly` jest przeznaczony do kontroli wdrożonego klienta Blazor WebAssembly
hostowanego przez Admin pod `/_client/`. Testy mogą być uruchamiane przeciw produkcji: nie
rejestrują kont, nie dodają produktów do koszyka, nie tworzą holdów ani wypożyczeń i nie otwierają
checkoutu.

## Zakres

- start środowiska Blazor WASM i pobranie `_framework/blazor.boot.json`;
- błędy JavaScript, błędy strony, nieudane żądania oraz odpowiedzi HTTP dla zasobów aplikacji;
- rzeczywiste karty `.product-card` z nazwą sprzętu, wypożyczalnią, odbiorem i ceną;
- wejście w szczegóły pierwszego produktu bez wykonywania akcji zapisującej;
- bezpośrednie wejście na publiczne trasy i kontrola linków tego samego hosta pod `/_client/`;
- brak poziomego overflow na home, katalogu i szczegółach przy 1440×1000 oraz 390×844;
- zgodnościowe przekierowanie `/products` do `/_client/products` z zachowaniem query stringu;
- pełnostronicowe screenshoty oraz tekstowy raport diagnostyczny dla każdego testu.

Testy nie zapisują nagłówków ani body odpowiedzi. Z raportowanych URL-i usuwany jest query string,
a typowe wartości tokenów i kluczy w komunikatach są redagowane.

## Uruchomienie na zdalnym runnerze Linux

```bash
cd SportRental.E2ETests/SportRental.E2ETests

export SR_ADMIN_URL='https://app.example.com'
export SR_E2E_RUN_ID="remote-$(date -u +%Y%m%dT%H%M%SZ)"
export SR_E2E_ARTIFACT_DIR="$PWD/artifacts/client-readonly"

dotnet restore
dotnet build --no-restore
pwsh bin/Debug/net10.0/playwright.ps1 install chromium

dotnet test --no-build \
  --settings client-readonly.runsettings \
  --filter 'TestCategory=ClientReadOnly' \
  --logger 'console;verbosity=normal' \
  --logger "trx;LogFileName=$SR_E2E_RUN_ID.trx"
```

`SR_ADMIN_URL` jest wymagany celowo — pakiet nie wybiera sam środowiska, żeby nie przetestować
przypadkowo niewłaściwego hosta. Do zmiany katalogu wyników służy `SR_E2E_ARTIFACT_DIR`.

Artefakty powstają w układzie:

```text
artifacts/client-readonly/<run-id>/<test-name>/
├── report.txt
├── home-desktop.png
├── catalog-desktop.png
└── ...
```

Jeżeli test przerwie się przed planowanym screenshotem, teardown próbuje dopisać `failure.png` i
zawsze zapisuje zgromadzony raport. Pakiet ma kategorię NUnit `ClientReadOnly` i jest oznaczony jako
nieparalelny, aby nie generować niepotrzebnego ruchu do testowanego hosta.
