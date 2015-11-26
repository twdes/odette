
Postfach für einen Partner.

# In

- Im Order werden die angenommenen Dateien abgelegt.
- Dateiformat (ORIGINATOR#VIRTUALNAME#ZEITSTEMPEL.STATE)
	- State
		- new: Datei wurde neu hinzugefügt
		- recv: Datei wurde vollständig empfangen und validiert
		- se2e: Bestätigung kann gesendet werden (dieser Status muss durch ein externes Program gesetzt werden)
		- done: Bestätigung gesendet, Datei wartet auf Bereinigung
- Datei ATTR enthält XML mit zusätzlichen Information zu Signatur und co.?
- Zusätzlich für den Status "done" die NERP informationen

# Out

- In den Ordner werden Dateien abgelegt, die versendet werden sollen
- Dateiformat (ORIGINATOR#VIRTUALNAME#ZEITSTEMPEL.STATE)
	- State
		- new: Bereit zum Senden (wird nicht vom Dienst verwaltet)
		- sent: Datei ist bereit zum senden (Zusatzdatei wurde geschrieben)
		- we2e: Datei wurde erfolgreich weitergegeben (warte auf EndToEnd)
		- re2e: EndToEnd Erhalten
		- done: Wird durch externes System gesetzt (bereit zum Bereinigen, wird nicht von Dienst übernommen)

# Format der Extented Datei
<oftp>
  <description format="<<format>>" maximumRecordSize fileSize fileSizeUnpacked>

	<<DESCRIPTION>
  </description>
  <send userData>
  <commit reasonCode reasonText userData>
  <records> <-- only for Variable -->
    <r o= l= />