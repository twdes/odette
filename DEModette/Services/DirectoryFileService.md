
Postfach für einen Partner.

# In

- Im Order werden die angenommenen Dateien abgelegt.
- Dateiformat (ORIGINATOR#VIRTUALNAME#ZEITSTEMPEL.STATE)
	- State
		- new: Datei wurde neu hinzugefügt
		- recv: Datei wurde vollständig empfangen und validiert
		- done: Bestätigung kann gesendet werden (dieser Status muss durch ein externes Program gesetzt werden)
		- arc: Bestätigung gesendet, Datei wartet auf Bereinigung
- Datei ATTR enthält XML mit zusätzlichen Information zu Signatur und co.?
- Zusätzlich für den Status "done" die NERP informationen

# Out

- In den Ordner werden Dateien abgelegt, die versendet werden sollen
- Dateiformat (VIRTUALNAME.F.ZEITSTEMPEL.STATE)
	- Lastwrite -> Angelegt
	- Access -> letzter Status
	- State
		- new: Bereit zum Senden
		- we2e: Datei wurde erfolgreich weitergegeben (warte auf EndToEnd)
		- re2e: EndToEnd Erhalten
		- done: Wird durch externes System gesetzt (bereit zum Bereinigen)