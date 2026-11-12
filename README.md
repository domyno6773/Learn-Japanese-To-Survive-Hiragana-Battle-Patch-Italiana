# Patch italiana per Learn Japanese To Survive! Hiragana Battle

[**Scarica l'installer per Windows**](https://github.com/domyno6773/Learn-Japanese-To-Survive-Hiragana-Battle-Patch-Italiana/releases/latest/download/Hiragana-Battle-Patch-Italiana.exe)

Traduzione amatoriale per l'edizione Steam di **Hiragana Battle**. Include menu, dialoghi, missioni e lezioni in italiano, schede di vocabolario tradotte, correzione dell'apostrofo e miglioramenti al fullscreen. La v1.1.0 include la revisione dei dialoghi nel contesto delle scene, la correzione di lezioni e descrizioni degli oggetti e l’allineamento dei puntini di sospensione.

## Installazione

1. Installa il gioco da Steam e chiudilo se è aperto.
2. Scarica ed esegui `Hiragana-Battle-Patch-Italiana.exe`.
3. L'installer cerca il gioco nelle librerie Steam. Se la cartella non è quella giusta, scegli **Sfoglia…** e indica la cartella che contiene `Learn Japanese To Survive - Hiragana Battle.exe`.
4. Premi **Installa la patch**, quindi riapri il gioco da Steam. **F4** passa al fullscreen.

La patch è stata preparata per la build Steam **4038798** su Windows. Se hai già la **v1.0.0**, avvia lo stesso installer per aggiornare direttamente. L'installer controlla l'integrità dei file prima di modificarli e lascia intatti i salvataggi. Windows può chiedere il permesso di scrivere nelle cartelle protette. L'eseguibile è privo di firma digitale.

## Ripristinare l'inglese

Con il gioco chiuso, usa **Proprietà → File installati → Verifica integrità dei file di gioco** in Steam. In alternativa, copia il contenuto di `Backup-patch-italiana` nella cartella del gioco, sostituendo i file quando richiesto. Steam può ripristinare i file originali anche durante un aggiornamento.

## Compilare l'installer

Esegui `build.ps1` da PowerShell su Windows con .NET Framework 4.5 o successivo. Lo script incorpora `patch.zip` nell'eseguibile e salva il risultato in `dist/`.

Per rigenerare `patch.zip` da una versione aggiornata della traduzione, passa a `make_patch.py` la cartella tradotta e gli archivi ZIP dei file originali, con percorsi relativi alla cartella del gioco. Per includere un aggiornamento diretto da una release precedente, aggiungi anche `--upgrade-from` seguito dal suo `patch.zip`.

```powershell
python make_patch.py "C:\Percorso\HiraganaBattle" "C:\Percorso\originali.zip"
.\build.ps1
```

Questa patch è indipendente dagli autori del gioco e richiede una copia del gioco già installata.

