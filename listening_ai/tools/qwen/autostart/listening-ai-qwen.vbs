' Puts the listening_ai model back up at logon, with nothing on screen.
'
' The phone looks for the model at a fixed address, and an address with nothing
' listening behind it is indistinguishable from a model that cannot answer - so
' the machine being on and logged in has to be enough for the server to be there.
' Until this existed, that was a step somebody had to remember every time.
'
' A startup shortcut rather than a scheduled task on purpose: tasks want
' elevation to register, and this belongs to whichever user is logged in rather
' than to the machine. It starts serve.ps1, which is idempotent - if a server is
' already answering it says so and exits, so a second logon cannot take the port.

Set shell = CreateObject("WScript.Shell")
script = "C:\Users\LorXer\Documents\trae_projects\FallenAngel\listening_ai\tools\qwen\serve.ps1"
command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & script & """"
' 0 = no window, False = do not wait for it to finish
shell.Run command, 0, False
