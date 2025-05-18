# peugeot308can
Приложение для работы с панелью приборов от Peugeot 308 (207) через протокол lawicel.
Принимает на вход данные в виде JSON через WebSockets на порту 1212

# work in progress!

JSON:
```
{
  "rpm": 1230,
  "speed": 123, # in kmh 
  "temp": 90, # in c
  "gas": 22, # % of gasoline
  "rturn": True,
  "lturn": True,
  "lbeam": True,
  "hbeam": False,
  "stop": False,
  "parking": True,
  "oilwarn": False,
  "battery": False,
  "esp": False,
  "check": False,
  "abs": False,
  "odo": 0.0, # not used
  "gear": 0, # from -1 to 6
  "fog": True
}
```
