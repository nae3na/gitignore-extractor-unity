# Gitignore Extractor

`.gitignore` に定義された無視対象を解析し、対応するファイル・フォルダを(metaを含む)、フォルダにコピーして書き出すUnity エディタ拡張ツールです。

---

## 概要

- `.gitignore`を自動的に解析します。
- 無視されたフォルダ/ファイル/.metaをコピーします。
- フォルダ構造を維持します。
- netation(!)を認識してコピーします。
- Unityエディターから2クリックで簡単でコピーできます。
- デフォルトはAssets以下の階層のみのコピーを行えます。
- Assets以外の階層も抽出してコピーが出来ます。

---

## 要件

- Unity 2017.1 以上
- Unity 2022.3LTSでテスト済み

---

## インストール

1. Unityプロジェクトを開きます。
1. `gitignore-extractor-unity/Assets/UniN3`フォルダをプロジェクトの`Assets`フォルダ内に配置します。

---

## 使用方法

1. Unityを開き、`Tools > Gitignore Extractor`でツールを開きます。
1. `Output folder name`から書き出すフォルダ名を指定します。
1. `Reflesh Preview`をクリックして、書き出す対象のパスを取得し、プレビューします。
1. `Ignored paths`を展開し、対象のパスを確認します。
1. `Export`をクリックする事でフォルダ/ファイルがコピーされます。(デフォルトはプロジェクト直下の"gitignore"フォルダにコピーされます)

### Assets以外の階層のファイルのコピー

`Top-level dirs to include besides "Assets"`から、`Add`ボタンを押してフォルダ名を入力することで、Assets階層以下のフォルダ/ファイル以外もコピーすることが出来ます。

例) `Packages/aaa`を.gitignoreに記載していて、コピーの対象にしたい場合は`Packages`を追加します。

## ライセンス

MIT License