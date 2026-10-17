# Estudar

Aplicativo para Windows para resolver provas oficiais do ENEM e consultar o acervo do PAS e do vestibular da UnB.

- Biblioteca com busca e filtros; leitura local de PDFs com página e zoom.
- Simulados ENEM 2009–2025 com respostas, confiança, marcações, tempo ativo e salvamento automático.
- Rascunho por questão com caneta, marca-texto, borracha, pressão, desfazer/refazer e janela destacável.
- Histórico, correção por área, revisão de erros e observações após corrigir.
- Exportação local em JSON e Markdown com PDFs originais e imagens dos rascunhos.
- Temas claro e escuro. Sem conta, API ou envio automático de dados.

## Usar

Baixe e extraia a versão Windows x64 em [Releases](https://github.com/LuisAlves10/Estudar/releases). Abra `Estudar.exe`. É necessário Windows 10/11 com [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). O pacote independente inclui o runtime .NET.

Execute `baixar-provas.bat` para obter os PDFs das fontes oficiais. O script confere tamanho e SHA-256, ignora arquivos íntegros, preserva arquivos divergentes e permite retomar executando-o novamente. É possível filtrar pelo PowerShell:

```powershell
./scripts/Download-Corpus.ps1 -Exam ENEM -Year 2025
```

O catálogo tem 581 documentos únicos; os arquivos ficam em `Acervo/ENEM`, `Acervo/PAS` e `Acervo/UnB`, com edição, etapa e tipo no nome. Nenhum PDF é distribuído no repositório ou na release. Alguns documentos antigos têm indicação de versão adaptada ou gabarito preliminar; confira os rótulos.

Após o download, o aplicativo funciona offline. Registros ficam em `Dados/Estudos`: banco SQLite, preferências, rascunhos e exportações. Para backup completo, feche o app e copie essa pasta. A ação de backup do banco nas configurações copia somente o SQLite.

## Compilar

Instale o SDK .NET 10 no Windows. Na raiz do repositório:

```powershell
dotnet run --project Estudo.Tests -c Release
./Publicar.ps1 -SelfContained
```

O executável estará em `Executavel/Estudar.exe`. Para desenvolver: `dotnet run --project Estudo.Windows`. Para um build que usa o .NET Desktop Runtime instalado, execute `./Publicar.ps1` sem `-SelfContained`.

`Estudo.Core` contém catálogo, tentativas, correção e exportação; `Estudo.Windows` contém a interface WPF, tinta e leitor PDF.js em WebView2. `catalogo` guarda metadados oficiais e referências de questões, sem enunciados. O programa também reconhece o layout legado com `Dados/PROVAS_MANIFEST.csv` e pastas de PDFs junto de `Aplicativo`.

## Limites

Acertos no ENEM não são nota TRI. Redação, PAS e UnB ainda não têm correção automática. O índice aponta a página inicial do enunciado, que pode continuar na próxima página; não há recorte automático ou classificação temática. Pressão da caneta depende do dispositivo e do driver; o rascunho tem papel de 1600 × 1200 com zoom e movimentação.

Respostas, anotações e desenhos originais ficam preservados após finalizar. Observações de correção são guardadas separadamente. Exportações contêm dados pessoais; nenhum arquivo é enviado pelo app.

Código sob licença MIT; dependências e documentos seguem suas próprias licenças. Veja [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
