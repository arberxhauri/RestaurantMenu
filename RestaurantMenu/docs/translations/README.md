# Translation review

One sheet per menu language (`review-sq.csv`, `review-it.csv`, `review-de.csv`, `review-fr.csv`, `review-es.csv`, `review-tr.csv`). Each has every guest-facing phrase on the menu, booking page, restaurant website, ordering, feedback and messages: 186 rows, generated from the code on 5 October 2026.

## For the reviewer

Open the file in Excel, Numbers or Google Sheets (File → Import). For each row:

- Read **English** and the **current** translation.
- Write `yes` in **OK?** if it reads naturally to a guest in a restaurant, or `fix` and your wording in **Better wording**.
- Keep `{0}`, `{1}` and so on exactly as they are. They are filled in with a time, a day, a number or the restaurant's name, which the English shows.
- Keep it short; most of these sit on a phone screen.
- Tone already used: German, Italian and Spanish address the guest informally (du / tu / tú); French and Turkish formally (vous / siz). Albanian uses the plural "ju". Say so if that is wrong for restaurants in your language.

## For the developer

Each row's **Where in the code** points to the string: `BookingText.All.NoSlots` is the `NoSlots` field of the `All` table in `Helpers/BookingText.cs`; `[3]` is the 4th item of a list, `Item1` the first part of a pair. `Views/Menu/Index.cshtml ui.*` rows are in the `ui` switch near the top of that view.

After applying fixes, run `dotnet test RestaurantMenu.Tests/RestaurantMenu.Tests.csproj`: `TranslationCoverageTests` fails if a language is missing, a text is empty, or a `{0}` placeholder was lost.

Not included: the offline page in `wwwroot/sw.js` (English and Albanian only, by design) and dish/category names, which restaurants write themselves.
