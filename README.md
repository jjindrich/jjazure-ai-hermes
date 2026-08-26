# Hermes: plánování víkendového výletu

Ukázka používá **Nous Research Hermes Agent** a model `gpt-5.4-mini` dostupný
přes GitHub Copilot. Z požadavku v `data/travel-request.txt` sestaví proveditelný
víkendový plán upravený podle počasí, času a rozpočtu.

## Co běží

- **Koordinátor programu** řídí celý běh a syntézu.
- **Plánovač trasy** připraví trasu, časy a záložní variantu.
- **Meteorolog** ověří předpověď a rizika z aktuálních webových zdrojů.
- **Rozpočtář** sestaví položkový rozpočet včetně 10% rezervy.
- Výsledek vznikne v `artifacts/weekend-plan.md`.

Hlavní Hermes vytvoří koordinátora s rolí `orchestrator`. Koordinátor paralelně
spustí tři specialisty, počká na jejich výsledky a vyřeší rozpory. Podagenti
nevidí historii rodiče, proto každý dostane kompletní zadání a omezení.

## Spuštění

Požadavky:

- Hermes Agent
- aktivní GitHub Copilot
- přihlášení dostupné příkazu `hermes auth status copilot`

```powershell
.\run-demo-copilot.ps1
```

Skript nakonfiguruje Hermes a vloží zadání z `prompts/travel-demo.txt` do
schránky. Potvrďte spuštění klávesou Enter; po otevření výzvy Hermes stiskněte
**Ctrl+V** a následně **Enter**. Interaktivní relace musí zůstat otevřená, aby
se do ní vrátil výsledek koordinátora.

Stav přihlášení:

```powershell
hermes auth status copilot
```

## Důležité soubory

| Soubor | Úloha |
|---|---|
| `data/travel-request.txt` | Vstupní požadavky a omezení výletu |
| `HERMES.md` | Role agentů a pravidla koordinace |
| `prompts/travel-demo.txt` | Spouštěcí instrukce pro hlavního agenta |
| `run-demo-copilot.ps1` | Konfigurace a spuštění přes GitHub Copilot |
| `artifacts/weekend-plan.md` | Výsledný koordinovaný plán |
