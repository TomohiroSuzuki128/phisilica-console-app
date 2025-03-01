# PhiSilicaConsoleApp
Phi Silica の Language Model を手軽に検証できるコンソールアプリのサンプルです。
- Windows Copilot Runtime API を使用します。
- C# で書かれています
　　
## 必要環境
- Copilot+ PC (Snapdragon X)
- Windows App SDK (1.7.0-experimental3)
- RAM 16GB 以上
- Windows 11 Insider Preview Build 26120.3073 (開発およびベータ チャネル)
　　
## 目的
- Phi Silica 検証の用途
- Phi Silica でエッジ AI アプリを作る前に、精度が出るか確認できる
- NPU を活用することができる

## 特長

### 設定ファイルは json

以下の項目を設定できます
- 翻訳の ON/OFF
- RAG の ON/OFF

### プロンプトを一度英語に翻訳して問い合わせ、結果を再度日本語にできる

Phi Silica は英語に最適化されているため、プロンプトを一度英語にして問い合わせ結果を再度日本語にすることで精度を高められる可能性があります。翻訳の ON/OFF は設定ファイルのオプションで指定します。
（現状あまり効果が出ていません）

### RAG に対応

md, txt で付加情報を与えることでで RAG に対応しています。特に翻訳した時の固有名詞を与えることで、精度を高められます。

ベクトル検索は Build5Nines 氏の [SharpVector](https://github.com/Build5Nines/SharpVector) を使用しています。

#### 処理フロー
1. システムプロンプトを英語に翻訳する
1. ユーザープロンプトを英語に翻訳する
1. 英語にしたプロンプトで問い合わせ
1. レスポンスでベクトルデータベースに問合せし、RAG のデータ取得
1. RAG のデータを付加して日本語に翻訳

## 設定ファイルのフォーマット

```json:settings.json
{
  "isTranslate": "<true or false>",
  "isUsingRag": "<true or false>",
  "systemPrompt": "<Your system prompt>",
  "userPrompt": "<Your user prompt>",
  "additionalDocumentsPath": "<Your documents path>" // RAG 用ファイルの Path
}
```