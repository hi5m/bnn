ECHO OFF
CLS
ECHO.
ECHO Commiting the Project...

SET CurrPath=%cd%
SET Comment=%1
CD ..\..\..\

ECHO ON
git commit -a -m %Comment%
git pull
git push
ECHO OFF
CD %CurrPath%

ECHO.