# Сторонние компоненты

| Компонент | Лицензия | Ссылка |
|---|---|---|
| NAudio.WinMM 2.2.1 | MIT | https://github.com/naudio/NAudio |
| Google.Cloud.Speech.V1 3.8.0 | Apache-2.0 | https://github.com/googleapis/google-cloud-dotnet |
| Microsoft.Web.WebView2 | BSD-3-Clause (лицензия Microsoft для WebView2 SDK) | https://www.nuget.org/packages/Microsoft.Web.WebView2 |
| Vosk 0.3.38 (NuGet) | Apache-2.0 | https://github.com/alphacep/vosk-api |
| Модель vosk-model-small-ru-0.22 (скачивается при первом запуске, в репозиторий не входит) | Apache-2.0 | https://alphacephei.com/vosk/models |
| .NET 8 runtime | MIT | https://github.com/dotnet/runtime |

Задания — авторские, составлены в формате ФИПИ; ФИПИ, ОГЭ, ЕГЭ, GigaChat, Google — названия их правообладателей, упомянуты только для описания совместимости.

Ключи API в репозитории не хранятся. Бесплатный голосовой режим: если в «Настройках» или в переменной окружения `RUSTUTOR_GOOGLE_KEY` задан ключ — используется Google, иначе офлайн-распознавание Vosk без ключа.
