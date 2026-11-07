# Translating OMSI MapWorks

OMSI MapWorks shows its interface in the language you choose (**Settings → General → Language**). This folder holds the texts.
Anyone can add a language or improve one; you only need a text editor and, for a pull request, a GitHub account.

Only the **interface** is translated. Map files, object and spline names, file paths, project files, exports, your own names and the
technical log stay exactly as they are, whatever language you choose.

## How the folder is built

```text
locales/
├── README.md              this file
├── languages.json         the overview: every language the program offers, one line per setting
├── en-US/                 the reference language: every key exists here first
│   ├── menus.json         texts of one area of the program
│   └── ...
├── de-DE/                 German: the same keys, German texts
└── pt-BR/                 (example) a language you added: its folder, plus its lines in languages.json
```

- `languages.json` lists **all languages**. It is the only place that says which languages exist; the program reads it at start, so a
  new language needs **no change in the code**. One flat object, every language has the lines `<code>.name`, `<code>.englishName` and
  `<code>.status`, and optionally `<code>.system` and `<code>.plural.<case>`:

  ```json
  {
    "de-DE.name": "Deutsch",
    "de-DE.englishName": "German",
    "de-DE.status": "approved",
    "de-DE.system": "German",
    "de-DE.plural.one": "i = 1 and v = 0"
  }
  ```

  | Setting | Meaning |
  |---|---|
  | `name` | the language written in itself (it is what the language list shows, never a flag) |
  | `englishName` | the same in English, for logs and the check |
  | `status` | `draft` while the translation is not finished, `approved` when the maintainers released it. **Only approved languages can be chosen** in the program (development builds offer drafts too). |
  | `system` | optional: the name of the operating system language that should start the program in this language the first time, as Unity names it (`German`, `Portuguese`, `Chinese`…); several names are separated by commas. Without it, the program still matches a language whose code starts like the computer's culture. |
  | `plural.<case>` | optional: when a text needs the singular or another form, which numbers count as which case (see below). |

- One folder per language, named exactly like its code (`de-DE`, `pt-BR`). It holds the texts: every `*.json` file in it is one JSON
  object whose keys are the **keys** and whose values are the **texts**. Which file a key lives in does not matter to the program, but
  keys must be unique across all files of a language.
- Keys look like `menu.file` or `tools.road.create`: a lower-case area first, then more parts joined with dots. They are stable names.
  Never translate or change a key; only the text behind it.
- A line like `"@menu.tools": "..."` (an `@` before a key) is a **note for translators** and is not shown in the program. Read the notes of
  the English files: they tell you where a text appears and what it means.

### Plural rules

For the languages English, German, Spanish, French, Polish and Czech the rules are in `languages.json` already. For another language you
write them in the notation of the Unicode CLDR, one line per case other than `other` (which needs no rule). Look your language up in the
[CLDR plural rules](https://www.unicode.org/cldr/charts/latest/supplemental/language_plural_rules.html) and copy the lines:

```json
"pt-BR.plural.one": "i = 0..1"
```

`i` is the integer part of the number, `v` the number of decimals shown, `n` the number itself, `%` the remainder, `..` a range, `,` a list,
`and`, `or`, `=` and `!=` combine them. Text after an `@` (the CLDR examples) is ignored. The cases you give are the cases every plural
text of that language has to have in the check, besides `other`. Without any `plural` line the program treats a language it does not
know as having `other` only, which is right for languages such as Japanese or Turkish, and the check says so for all others.

## Placeholders

Texts can contain placeholders in curly braces, in the ICU MessageFormat style:

| You write | Meaning |
|---|---|
| `{name}` | a value the program puts in, such as a file name. Keep the name exactly as in English. |
| `{count, number}` | a number written the way your language writes numbers (`1,234.5` or `1.234,5`) |
| `{when, date, short}` `{when, date, medium}` `{when, time, short}` | a date or time in your language's style |
| `{count, plural, =0 {…} one {…} other {…}}` | the text that fits the number; `#` inside stands for the number |
| `{kind, select, road {…} other {…}}` | a text chosen by a word the program gives |

Rules:

- A translation has **the same placeholders as the English text**: no more, no fewer, same names, same kinds.
- A `plural` always needs the case `other`, and one case for every `plural.<case>` rule of your language in `languages.json`: German and
  English `one` and `other`; Polish `one`, `few`, `many`, `other`; Czech `one`, `few`, `many`, `other`; French, Spanish and Portuguese
  `one` and `other`. The exact cases `=0`, `=1` are allowed in every language.
- A single apostrophe is just an apostrophe (`variant's`, `can't`, `l'objet`). To show a brace itself, put it in apostrophes: `'{'`.
  To show two apostrophes in a row, write four. Do not type apostrophes around anything else.
- Do not translate the names that are placeholders and do not put spaces or line breaks into JSON strings; a line break is written `\n`.
- File names and names of your own are put in by the program unchanged. Do not try to change them in the text.

## Technical terms

Keep OMSI's own words as they are in the game and in OMSI's editor when your language's OMSI uses them: *spline*, *tile*, *chrono*,
*global.cfg*, *.sco*, *.sli*, file and folder names, and the names of the OMSI versions. Words that are only the program's own
(*Inspector*, *Project browser*, *Priority*, *Lanes*) are translated, ideally as the OMSI community already says them. If you are
unsure, leave a note in your pull request; a consistent term is better than a perfect one.

## Adding a language

Example: Brazilian Portuguese, `pt-BR`.

1. Copy the folder `en-US` to `pt-BR` (the name is the language code).
2. Add the lines for it to `languages.json` (the status stays `draft`):

   ```json
   "pt-BR.name": "Português (Brasil)",
   "pt-BR.englishName": "Portuguese (Brazil)",
   "pt-BR.status": "draft",
   "pt-BR.system": "Portuguese",
   "pt-BR.plural.one": "i = 0..1"
   ```

3. Translate the **values** in each file of the folder. Leave the keys and the placeholders. You may start with one file (delete the
   others from your copy); texts you have not translated yet show in English.
4. Run the check (below) and fix what it reports.
5. Open a pull request. The maintainers set `status` to `approved` once the language is complete enough to be offered.

Nothing else is needed: no code, no list in a settings file, no build step. To remove a language, delete its lines and its folder.

## Changing a translation

Edit the text in the file of that language, keep the placeholders, run the check, open a pull request that says what you changed and why.

## Checking your files

The check reads every language against the English one. It finds JSON mistakes (with the line), keys written twice, texts that are not
texts, unknown keys, missing keys (an error for an approved language, a note for a draft), placeholders that differ from the English
text and plural texts that lack a case your language needs.

```text
dotnet run --project Tools/LocaleCheck -- locales
```

It prints every finding as `error: de-DE/menus.json(12): [menu.file] …` and exits with an error code when there is a real error.
`--strict` also treats notes as errors. The same check runs on every pull request that changes `locales/` (GitHub Actions,
`.github/workflows/locales.yml`), and the program's release build refuses to ship a released language with errors.

## Trying your translation

A built OMSI MapWorks keeps the files in `OMSIMapWorks_Data/StreamingAssets/locales`, `languages.json` included. Replace them there
(or add your language folder and its lines in `languages.json`) and restart to see your changes; an update brings the official files back. Development builds also offer draft languages. `-language de-DE` on the command line
starts the program in a language for one run without saving the choice.
