# Translating OMSI MapWorks

OMSI MapWorks shows its interface in the language you choose (**Settings → General → Language**). This folder holds the texts.
Anyone can add a language or improve one; you only need a text editor and, for a pull request, a GitHub account.

Only the **interface** is translated. Map files, object and spline names, file paths, project files, exports, your own names and the
technical log stay exactly as they are, whatever language you choose.

## How the folder is built

```text
locales/
├── README.md              this file
├── en-US/                 the reference language: every key exists here first
│   ├── locale.json        what the language is (see below)
│   ├── menus.json         texts of one area of the program
│   └── ...
├── de-DE/                 German: the same keys, German texts
└── pl-PL/                 a draft language: only locale.json so far
```

- One folder per language, named with its language tag (`de-DE`, `pl-PL`, `en-US`).
- `locale.json` describes the language:

  ```json
  {
    "code": "de-DE",
    "name": "Deutsch",
    "englishName": "German",
    "status": "approved"
  }
  ```

  `name` is the language written in itself (it is what the language list shows, never a flag). `status` is `draft` while the translation
  is not finished and `approved` when the maintainers released it. **Only approved languages can be chosen** in the program.
- Every other `*.json` file in the folder holds texts: one JSON object whose keys are the **keys** and whose values are the **texts**.
  Which file a key lives in does not matter to the program, but keys must be unique across all files of a language.
- Keys look like `menu.file` or `tools.road.create`: a lower-case area first, then more parts joined with dots. They are stable names.
  Never translate or change a key; only the text behind it.
- A line like `"@menu.tools": "..."` (an `@` before a key) is a **note for translators** and is not shown in the program. Read the notes of
  the English files: they tell you where a text appears and what it means.

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
- A `plural` always needs the case `other`. Add the cases your language uses: German and English `one` and `other`; Polish `one`, `few`,
  `many`, `other`; Czech `one`, `few`, `other` (and `many` for fractions); French and Spanish `one` and `other`. The exact cases
  `=0`, `=1` are allowed in every language.
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

1. Copy `en-US` to a new folder with the language tag, for example `fr-FR`.
2. Edit `locale.json`: set `code`, `name` (in the language itself), `englishName`, and keep `"status": "draft"`.
3. Translate the **values** in each file. Leave the keys and the placeholders. You may start with one file; texts you have not translated yet
   show in English.
4. Run the check (below) and fix what it reports.
5. Open a pull request. The maintainers set `status` to `approved` once the language is complete enough to be offered.

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

A built OMSI MapWorks keeps the files in `OMSIMapWorks_Data/StreamingAssets/locales`. Replace them there and restart to see your
changes; an update brings the official files back. Development builds also offer draft languages. `-language de-DE` on the command line
starts the program in a language for one run without saving the choice.
