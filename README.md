# Gitignore Extractor

[![Readme_JA](https://img.shields.io/badge/README-日本語版-blue)](./README_JA.md)

A Unity Editor extension tool that parses `.gitignore` and copies the corresponding ignored files and folders (including `.meta`) into a specified folder.

---

## Overview

- Automatically parses `.gitignore`.
- Copies ignored folders, files, and `.meta` files.
- Preserves the original folder structure.
- Supports negation (`!`) rules in `.gitignore`.
- Simple two-click operation from the Unity Editor.
- By default, only copies files under the `Assets` hierarchy.
- Can also extract and copy folders outside of `Assets`.

---

## Requirements

- Unity 2017.1 or later  
- Tested with Unity 2022.3 LTS

---

## Installation

1. Open your Unity project.  
2. Place the `gitignore-extractor-unity/Assets/UniN3` folder inside your project's `Assets` folder.

---

## Usage

1. In Unity, open the tool via `Tools > Gitignore Extractor`
2. Specify the folder name in **Output folder name**.
3. Click **Reflesh Preview** to load and preview the paths that will be exported.
4. Expand **Ignored paths** to review the target paths.
5. Click **Export** to copy the folders/files.  
   (By default, they will be copied into a `gitignore` folder in your project root.)

---

### Copying Files Outside the Assets Folder

From **Top-level dirs to include besides "Assets"**, click the **Add** button and enter a folder name to include files outside the `Assets` directory.

Example:
If your `.gitignore` contains `Packages/aaa` and you want to copy it, add `Packages` as an extra top-level directory.

---

## License

This project is licensed under the MIT License.
