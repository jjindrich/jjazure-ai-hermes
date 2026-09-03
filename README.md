# Multi-agentní plánování víkendového výletu

Repozitář obsahuje dvě oddělené varianty stejného scénáře:

- `hermes/` používá **Nous Research Hermes Agent**,
- `openclaw/` používá **OpenClaw** v headless režimu.

Obě varianty používají model `gpt-5.4-mini` z GitHub Copilot, stejný vstup
`data/travel-request.txt` a stejnou hierarchii čtyř agentů. OpenClaw používá
oficiální Copilot SDK harness nad přihlášeným GitHub Copilot CLI.

Hermes ani OpenClaw nejsou samy o sobě režim „autopilot“. Jsou to agentní
runtime/orchestrátory. Autopilot obvykle označuje míru autonomie běhu; zde oba
runtime dostanou úkol a samostatně jej provedou pomocí nástrojů a podagentů.

## Společná hierarchie

- **Koordinátor programu** řídí celý běh a syntézu.
- **Plánovač trasy** připraví trasu, časy a záložní variantu.
- **Meteorolog** ověří předpověď a rizika z aktuálních webových zdrojů.
- **Rozpočtář** sestaví položkový rozpočet včetně 10% rezervy.
- Každá varianta zapisuje výsledek do vlastní složky `artifacts/`.

Hlavní agent vytvoří koordinátora. Koordinátor paralelně spustí tři specialisty,
počká na jejich výsledky a vyřeší rozpory. Podagenti nevidí historii rodiče,
proto každý dostane kompletní zadání a omezení.

## Hermes

Požadavky:

- Hermes Agent
- aktivní GitHub Copilot
- přihlášení dostupné příkazu `hermes auth status copilot`

```powershell
.\hermes\run-demo-copilot.ps1
```

Skript nakonfiguruje Hermes a vloží zadání z `hermes/prompts/travel-demo.txt` do
schránky. Potvrďte spuštění klávesou Enter; po otevření výzvy Hermes stiskněte
**Ctrl+V** a následně **Enter**. Interaktivní relace musí zůstat otevřená, aby
se do ní vrátil výsledek koordinátora.

Stav přihlášení:

```powershell
hermes auth status copilot
```

Výsledek: `hermes/artifacts/weekend-plan.md`.

## OpenClaw

Požadavky:

- Node.js 22.19 nebo novější,
- aktivní GitHub Copilot,
- přihlášený GitHub Copilot CLI:

```powershell
copilot login
```

Spuštění:

```powershell
.\openclaw\install.ps1
.\openclaw\run-demo-copilot.ps1
```

Sledování průběhu lze zapnout parametrem `-Monitor`:

```powershell
# Události Gateway přímo v aktuálním terminálu
.\openclaw\run-demo-copilot.ps1 -Monitor Terminal -LogLevel debug

# Interaktivní OpenClaw TUI v aktuálním terminálu
.\openclaw\run-demo-copilot.ps1 -Monitor Tui

# OpenClaw Control UI v prohlížeči
.\openclaw\run-demo-copilot.ps1 -Monitor Browser
```

Režim `Tui` je výchozí; pro běh bez monitoru použijte `-Monitor Quiet`.
`Terminal` podporuje úrovně `info`, `debug` a `trace`; `debug` je vhodný pro
sledování volání nástrojů a práce podagentů. Control UI zůstává připojené po
dobu běhu.
V TUI lze příkazem `/subagents list` zobrazit koordinátora a specialisty.
TUI ukončíte `/exit` nebo `Ctrl+D`; běh pak počká na vytvoření výsledného plánu.
První spuštění Gateway může trvat přibližně minutu a první úloha se může objevit
po další minutě inicializace Copilot runtime; skript během startu každých pět
sekund zobrazuje průběh.

Instalační skript sestaví připnutý oficiální tag OpenClaw `v2026.6.34` do
ignorované složky `.openclaw-runtime/`. Spouštěcí skript instalaci v případě
potřeby provede automaticky, spustí neinteraktivní
`openclaw agent` přes dočasnou lokální Gateway, uloží plán a automaticky ověří
povinné části i použití
`sessions_spawn`. Výsledek je `openclaw/artifacts/weekend-plan.md`; technický
souhrn běhu je `openclaw/artifacts/run-result.json`.

## Důležité soubory

| Soubor | Úloha |
|---|---|
| `data/travel-request.txt` | Vstupní požadavky a omezení výletu |
| `hermes/HERMES.md` | Role a pravidla pro Hermes |
| `hermes/run-demo-copilot.ps1` | Spuštění Hermes |
| `openclaw/AGENTS.md` | Role a pravidla pro OpenClaw |
| `openclaw/openclaw.json` | Reprodukovatelná konfigurace OpenClaw |
| `openclaw/install.ps1` | Instalace připnutého oficiálního tagu OpenClaw |
| `openclaw/run-demo-copilot.ps1` | Headless spuštění a kontrola OpenClaw |
| `openclaw/test-result.ps1` | Kontrola plánu a delegace |
