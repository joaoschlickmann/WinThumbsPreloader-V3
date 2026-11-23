# DeepSearch — guia de identidade visual para uma família de aplicativos

**Referência:** DeepSearch Cloud desktop, versão 0.2.7, código analisado em 01/10/2026.  
**Objetivo:** permitir que uma IA ou uma equipe construa o frontend de outro produto com a mesma identidade visual.  
**Nome da linguagem visual:** **Polished Obsidian**, com superfícies de **Liquid Light**.  
**Referência principal:** o aplicativo desktop atual. O painel administrativo tem uma variante mais densa, descrita no apêndice.

Este documento é autossuficiente: contém valores de cores, medidas, comportamentos, receitas CSS e um prompt de implementação. Não exige acesso ao repositório para reproduzir o estilo.

## 1. Como usar este documento

Para gerar outro app, entregue este arquivo inteiro à IA junto com a descrição das funcionalidades, telas e nome do novo produto. Peça que ela preserve a identidade e adapte apenas a estrutura necessária às funções desse produto.

As informações têm duas classificações:

- **Implementado:** valores e comportamentos encontrados no frontend atual; são a referência para fidelidade visual.
- **Recomendação para a família:** adaptação proposta para tornar a identidade reutilizável, responsiva ou mais acessível. Não significa que o desktop atual já implemente essa recomendação.

Quando houver diferenças entre regras gerais e receitas de componentes, a receita específica prevalece. Por exemplo: cards de imagem têm raio de 8 px, enquanto painéis de configurações têm raio de 24 px.

### Prioridades para reproduzir a identidade

1. Paleta neutra, hierarquia de contraste e temas.
2. Fonte de sistema, escala tipográfica e espaçamento das letras.
3. Materiais de vidro, bordas discretas e formas arredondadas.
4. Cabeçalho flutuante, área central ampla e controles compactos.
5. Ícones Lucide e estados de interação.
6. Movimento contido e conteúdo aparecendo sem atrasos artificiais.

## 2. Personalidade visual

O aplicativo é uma ferramenta de trabalho voltada a imagens e arquivos. Sua interface funciona como uma moldura silenciosa para o conteúdo: fundo escuro, poucos elementos simultâneos, tipografia precisa e ações claras.

| Característica | Tradução visual |
| --- | --- |
| Foco no conteúdo | Imagens e dados ocupam a área principal; a interface usa cores neutras. |
| Precisão | Alinhamentos claros, unidades pequenas de espaçamento, metadados compactos. |
| Sofisticação discreta | Vidro sutil, preto profundo, cantos suaves e bordas finas. |
| Rapidez | Feedback imediato, carregamento localizado, miniaturas antes dos detalhes. |
| Profundidade | Camadas de luminosidade e transparência; sombras concentradas em sobreposições. |
| Familiaridade | Controles reconhecíveis, ícones de linha e rótulos objetivos. |

**Descrição para uma IA:** ferramenta profissional de produtividade, com aparência de galeria fotográfica em uma sala escura; monocromática, limpa e tátil. A interface deve parecer parte do mesmo conjunto mesmo quando o conteúdo do novo app não for fotográfico.

## 3. Paleta oficial do desktop

### 3.1 Tema escuro — implementado

| Token CSS | Valor exato | Aplicação |
| --- | --- | --- |
| `--ds-base-black` | `#000000` | Fundo principal e texto de botões claros. |
| `--ds-obsidian` | `#050505` | Superfície escura complementar, disponível no sistema. |
| `--ds-surface` | `#131313` | Painéis sólidos, como o popover de filtros. |
| `--ds-surface-dim` | `#0e0e0e` | Rodapé e superfícies secundárias baixas. |
| `--ds-surface-bright` | `#2a2a2a` | Grupos de controles e superfícies elevadas. |
| `--ds-primary` | `#ffffff` | Texto principal, ícones ativos, ações primárias e progresso. |
| `--ds-muted` | `#888888` | Metadados, instruções e controles inativos. |
| `--ds-glass-bg` | `rgba(255, 255, 255, 0.02)` | Vidro de base e fundos de controles discretos. |
| `--ds-glass-border` | `rgba(255, 255, 255, 0.08)` | Contornos e trilhos de progresso. |
| `--ds-glass-active` | `rgba(255, 255, 255, 0.15)` | Hover, contorno ativo e destaque de superfície. |
| `--ds-error` | `#ffb4ab` | Texto/ícone de erro ou ação destrutiva. |
| `--ds-error-bg` | `rgba(255, 180, 171, 0.1)` | Fundo de mensagem de erro. |
| `--ds-error-border` | `rgba(255, 180, 171, 0.3)` | Contorno de erro. |
| `--ds-warning` | `#ffdc9c` | Aviso e estado pausado. |
| `--ds-warning-bg` | `rgba(255, 170, 50, 0.1)` | Fundo de aviso. |
| `--ds-warning-border` | `rgba(255, 170, 50, 0.3)` | Contorno de aviso. |

**Atenção à semântica de `primary`:** é a cor de maior contraste da interface, que muda entre os temas. Não é uma cor de marca azul ou outra cor saturada.

A operação concluída normalmente continua monocromática: `Check` ou `CheckCircle2`, texto e cor neutra. O desktop não define um token global de sucesso verde.

### 3.2 Tema claro — implementado

| Token CSS | Valor exato | Aplicação |
| --- | --- | --- |
| `--ds-base-black` | `#f8fafc` | Fundo principal claro; o nome do token é histórico. |
| `--ds-obsidian` | `#ffffff` | Superfície complementar branca. |
| `--ds-surface` | `#f1f5f9` | Painéis e preenchimentos. |
| `--ds-surface-dim` | `#e2e8f0` | Rodapé e regiões secundárias. |
| `--ds-surface-bright` | `#ffffff` | Superfície elevada. |
| `--ds-primary` | `#0f172a` | Texto principal, ação primária e ícone ativo. |
| `--ds-muted` | `#475569` | Texto secundário. |
| `--ds-glass-bg` | `rgba(0, 0, 0, 0.03)` | Vidro de base. |
| `--ds-glass-border` | `rgba(0, 0, 0, 0.08)` | Contorno sutil. |
| `--ds-glass-active` | `rgba(0, 0, 0, 0.15)` | Hover e contorno ativo. |
| `--ds-error` | `#ba1a1a` | Erro. |
| `--ds-error-bg` | `rgba(186, 26, 26, 0.1)` | Fundo de erro. |
| `--ds-error-border` | `rgba(186, 26, 26, 0.3)` | Contorno de erro. |
| `--ds-warning` | `#995500` | Aviso. |
| `--ds-warning-bg` | `rgba(230, 120, 0, 0.1)` | Fundo de aviso. |
| `--ds-warning-border` | `rgba(230, 120, 0, 0.3)` | Contorno de aviso. |

No tema claro, o botão primário fica escuro com texto claro. A mudança ocorre pela combinação `background: var(--ds-primary)` e `color: var(--ds-base-black)`.

### 3.3 Cores sobre imagens

Alguns elementos permanecem escuros mesmo no tema claro, para garantir leitura sobre fotografias:

- Badge da imagem: fundo `rgba(0,0,0,0.6)`, texto `#ffffff`.
- Ações sobre imagem: fundo `rgba(0,0,0,0.4)`, contorno branco a 20%, ícone branco.
- Overlay de informações: gradiente preto transparente, texto branco.
- Visualizador de imagens: ambiente escuro fixo.

Esses são **tratamentos locais de mídia**, não uma terceira paleta para o restante do app.

## 4. Tipografia

### 4.1 Fonte realmente usada

O frontend atual usa esta pilha:

```css
--ds-font: system-ui, -apple-system, "Segoe UI", Roboto,
  "Helvetica Neue", Arial, sans-serif;
```

No Windows, a aparência normalmente é determinada pela fonte de sistema, como Segoe UI. A interface aplica `-webkit-font-smoothing: antialiased`.

Os documentos antigos citam Inter, mas o desktop atual não carrega Inter como fonte remota. Para fidelidade ao app instalado, use a pilha acima. Se a família precisar de uma fonte idêntica entre plataformas, **recomendação:** distribuir Inter localmente via WOFF2 e adotar essa mudança em todos os produtos do conjunto; não aplicar a troca apenas ao novo app.

### 4.2 Escala observada

| Uso | Tamanho | Peso | Tracking / observação |
| --- | --- | --- | --- |
| Marca no cabeçalho | 18 px | 600 | `-0.02em` |
| Título de página / resultados | 24 px | 500 | `-0.02em` |
| Título do login | 28 px | 500 | `-0.02em` |
| Título de modal | 18–20 px | 600 | Normal ou discreto. |
| Título de seção em configurações | 14 px | 600 | Uppercase, `0.15em`, cor muted. |
| Nome de configuração | 16 px | 500 | Cor principal. |
| Descrição / corpo | 13–14 px | 400 | Line-height em torno de 1.4–1.5 onde definido. |
| Texto do dropzone | 16 px | 500 | `-0.01em` |
| Campo de busca | 14 px | 400 | Texto normal, sem uppercase. |
| Campos do login | 15 px | 400 | Texto normal. |
| Nome de arquivo no card | 13 px | 500 | Uma linha com ellipsis. |
| Rótulo técnico compacto | 11–12 px | 600 | Uppercase, `0.05em` a `0.15em`. |
| Contagem/badge | 11 px | 600 | `0.05em` |
| Nome de pasta | 15 px | 500 | Uma linha com ellipsis. |
| Caminho de pasta | 11 px | 600 | `0.05em`, muted. |
| Rodapé de status | 11 px | 600 | Uppercase, `0.15em`. |

### 4.3 Regras tipográficas para o novo produto

- Títulos de páginas com 24 px e peso 500; o conteúdo, não um título gigantesco, é o foco.
- Peso 600 para ações e metadados curtos; evitar usar negrito forte em todos os textos.
- Uppercase em rótulos técnicos, grupos de configurações e ações compactas.
- Texto corrido e entrada do usuário em caixa normal.
- Metadados em muted; valores relevantes em primary.
- Ellipsis para nomes e caminhos em regiões compactas; oferecer o texto completo em detalhe ou tooltip.
- Usar a mesma fonte nos controles (`font: inherit`) para não misturar o padrão do navegador com a tipografia do app.

**Recomendação de acessibilidade:** quando 11 px for pequeno para o contexto do novo app, aumentar para 12–13 px mantendo peso, tracking e hierarquia. O tamanho pequeno não deve prejudicar informação indispensável.

## 5. Espaçamento e proporções

A base de organização é um ritmo de 4 px. O código contém exceções pequenas para ajustes ópticos e controles específicos.

| Espaço | Uso habitual |
| --- | --- |
| 4 px | Padding interno de segmentos, pequenos intervalos. |
| 6–8 px | Ícone + texto, metadados, ações compactas. |
| 10–12 px | Controles lado a lado, linhas de formulários, badges. |
| 16 px | Padding de popover, espaços entre elementos de um grupo. |
| 20 px | Gap de formulário, padding de cards de lista. |
| 24 px | Padding de painel, grid de imagens, margem lateral do cabeçalho. |
| 32 px | Modal e agrupamentos especiais. |
| 40 px | Padding vertical da área principal, rodapé lateral. |
| 80 px | Respiro do estado vazio. |

**Recomendação:** criar tokens de espaçamento com esses valores. Esses tokens de espaçamento não existem hoje como variáveis globais do desktop; consolidá-los no novo produto é uma organização de implementação, não uma mudança de aparência.

## 6. Formas e raios

| Elemento implementado | Raio |
| --- | --- |
| Busca, botões principais, filtros, tabs e seletor de tema | `100px` / formato pill. |
| Botões de ícone sobre imagem | `50%` / círculo. |
| Card de imagem e skeleton de imagem | `8px`. |
| Badge de similaridade | `100px`. |
| Cabeçalho flutuante | `16px`. |
| Card de pasta | `16px`. |
| Popover de filtros | `12px`. |
| Painéis de configurações e plano | `24px`. |
| Dropzone | `24px`. |
| Card de login | `24px`. |
| Campos de login | `14px`. |
| Campo URL/textarea em configurações | `12px`. |
| Modal de servidor no login | `20px`. |
| Botões do modal de atualização | `4px`, exceção local. |
| Select de limite | `4px`. |

A identidade combina **controles pill**, **grandes painéis suaves** e **cards de conteúdo mais precisos**. Aplicar o mesmo raio gigante em todos os elementos descaracteriza essa relação.

## 7. Materiais, vidro e profundidade

### 7.1 Vidro padrão — implementado

```css
.glass-panel {
  background-color: var(--ds-glass-bg);
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  box-shadow: inset 0 0 0 1px var(--ds-glass-border);
}
```

O efeito é muito discreto: o fundo branco tem apenas 2% de opacidade no tema escuro. O contorno é um `box-shadow` interno de 1 px. Muitos componentes também acrescentam uma borda própria; reproduzir conforme a receita daquele componente.

**Importante:** o blur desfoca o que está atrás do painel. Sobre um fundo uniforme, seu efeito visual é naturalmente pequeno. O efeito de material também depende do contorno e da diferença de luminosidade.

### 7.2 Hierarquia de profundidade

| Camada | Tratamento |
| --- | --- |
| Canvas | Cor base uniforme. |
| Painel de trabalho | Vidro discreto ou superfície neutra. |
| Controle ativo | Contraste invertido ou luminosidade elevada. |
| Popover | Superfície sólida, blur 20 px, contorno e sombra localizada. |
| Modal | Fundo escurecido e desfocado; painel central sólido/vidro. |
| Lightbox | Fundo quase opaco escuro e blur 40 px. |

### 7.3 Sombras observadas

- A maioria dos painéis usa somente a borda interna do vidro.
- Popover de filtros: `0 10px 40px rgba(0,0,0,0.5)`.
- Imagem ampliada: `0 20px 60px rgba(0,0,0,0.8)`.
- Texto do card sobre imagem: `0 1px 4px rgba(0,0,0,0.5)`.
- Badge no lightbox: brilho leve `0 0 12px rgba(255,255,255,0.05)`.

Sombras são uma ferramenta localizada de sobreposição. Não acrescentar sombras grandes em todos os cards de trabalho.

**Recomendação de compatibilidade:** se `backdrop-filter` não estiver disponível, usar um preenchimento neutro suficientemente opaco; conservar borda, raio e contraste. Evitar aplicar `opacity` ao painel inteiro, pois isso também reduz a leitura do conteúdo.

## 8. Estrutura da janela e navegação

### 8.1 Shell desktop — implementado

```text
┌──────────────────────────────────────────────────────────────┐
│ margem superior 16 px                                       │
│  ┌ Cabeçalho de vidro / altura 64 px / raio 16 px ──────────┐ │
│  │ [ícone] Nome do app                 [Busca] [Ajustes]   │ │
│  └────────────────────────────────────────────────────────┘ │
│ espaço de 16 px                                             │
│                                                            │
│           Conteúdo central / largura 90%                    │
│           max-width: 1400 px                                │
│           Área com rolagem própria                          │
│                                                            │
├────────────────────────────────────────────────────────────┤
│ Rodapé fixo na composição / altura 40 px / status compacto  │
└────────────────────────────────────────────────────────────┘
```

- Shell em Flexbox vertical, `height: 100vh`, largura 100%.
- `overflow: hidden` no shell e no documento; a rolagem fica dentro das áreas de trabalho.
- Cabeçalho com margens `16px 24px 0`, padding horizontal 24 px, altura 64 px.
- Marca à esquerda: ícone de 24 px, nome de 18 px, gap de 12 px.
- Navegação à direita: ícones de 20 px, gap de 20 px, sem fundo permanente.
- Ativo em primary; inativo em muted; transição de cor 200 ms.
- Área principal abaixo do cabeçalho com espaço de 16 px.
- Conteúdo de busca e configurações: largura 90%, máximo 1400 px, centralizado, padding vertical 40 px.
- Rodapé: altura 40 px, fundo surface-dim, linha superior glass-bg, padding lateral 40 px.
- Janela inicial configurada em 1024 × 768 px; esse tamanho é uma referência de composição, não uma largura fixa para o novo frontend.

### 8.2 Adaptação ao outro aplicativo

Preservar a moldura, a marca compacta, as ações e os materiais. O novo app pode usar lista, editor, tabela ou dashboard no lugar da galeria.

**Recomendação:** se houver mais destinos de navegação, usar uma navegação textual compacta ou sidebar neutra quando necessário. Não adicionar sidebar apenas para preencher a tela. Os nomes, o ícone de marca e a estrutura funcional podem mudar; os tokens e o tratamento dos controles permanecem.

## 9. Busca e área inicial

### 9.1 Composição — implementada

Antes da primeira busca, o bloco principal fica centralizado verticalmente. Depois da busca, o conteúdo alinha ao topo e os resultados ocupam o espaço disponível.

- Bloco de busca com 70% da largura do conteúdo central.
- Dropzone acima do campo de texto.
- Espaço de 12 px entre dropzone e linha de busca.
- Campo de texto e botão principal lado a lado, gap de 12 px.
- Campo ocupa o espaço restante; botão tem padding horizontal de 16 px.
- Busca sticky, `top: 20px` após uma busca, `z-index: 10`.
- No estado inicial, existe um ajuste óptico `translateY(-63px)` no bloco; é específico da composição atual e não precisa ser copiado para telas com outra estrutura.

### 9.2 Campo de busca

- Forma pill.
- Padding `16px 20px 16px 52px`.
- Ícone Search de 20 px em muted, colocado a 24 px da esquerda.
- Texto de 14 px em primary.
- Fundo glass-bg e contorno glass-border.
- No foco, o contorno fica primary.
- Campo rotulado por `aria-label`, com placeholder curto.

### 9.3 Botão de busca

- Fundo primary, texto base-black, peso 600, tamanho 11 px.
- Tracking `0.05em`, formato pill.
- Hover: opacidade 0.9 e escala 1.02.
- Durante busca: superfície glass-bg, texto muted, cursor not-allowed, Loader2 de 14 px e texto de ação em andamento.
- Manter a largura do controle estável o suficiente para não deslocar toda a linha ao trocar o rótulo.

### 9.4 Cabeçalho que recolhe com a rolagem

O desktop usa duas versões sobrepostas na mesma célula de CSS Grid: a completa e a pill compacta. Isso conserva a altura do bloco durante a transição.

- Timeline CSS: `animation-timeline: scroll(nearest block)`.
- Intervalo de recolhimento: primeiros 120 px de rolagem.
- Versão compacta entra com `translateY(-20px) scale(0.95)` até o estado normal.
- Versão completa sai com `translateY(20px) scale(0.95)`.
- IntersectionObserver identifica topo, transição e estado recolhido.
- Nos estados estáveis, as animações são desativadas para preservar corretamente o blur dos descendentes.
- A camada inativa tem `aria-hidden`, `pointer-events` e disponibilidade de foco controlados.
- Sem suporte a scroll timeline, a versão completa continua disponível.

**Recomendação:** usar esse comportamento apenas quando o novo produto tiver um bloco de comando grande que valha a pena recolher. O princípio transferível é preservar a geometria durante o movimento.

## 10. Dropzone

**Implementado:** um botão grande, clicável, também usado para arrastar arquivos.

| Propriedade | Valor |
| --- | --- |
| Largura | 100% do bloco de busca. |
| Altura | 154 px. |
| Raio | 24 px. |
| Contorno | 2 px dashed, muted em repouso. |
| Material | Transparente em repouso, vidro no hover/drag. |
| Blur | 20 px. |
| Ícone | UploadCloud, 28 px. |
| Suporte do ícone | Círculo de 56 × 56 px, glass-bg. |
| Distância ícone → título | 20 px. |
| Título | 16 px / 500 / primary. |
| Texto secundário | 13 px / muted / tracking 0.02em. |

No hover ou drag, o contorno passa a primary. No foco de teclado, há outline de 2 px com offset de 4 px. Todo o componente deve responder ao clique e ao teclado, sem um falso botão que só funciona ao clicar no texto.

No novo app, substituir a ação e a mensagem conforme o tipo de conteúdo, mantendo o desenho.

## 11. Galeria, cards e mídia

### 11.1 Grid — implementado

```css
.media-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 24px;
  width: 100%;
  padding-bottom: 40px;
}
```

- 240 px é o tamanho mínimo padrão de miniatura.
- O usuário ajusta esse mínimo entre 120 e 600 px.
- O grid é fluido; a largura efetiva do card pode crescer para preencher a linha.
- A implementação é `auto-fill`, e não uma grade fixa de 12 colunas.

### 11.2 Card de imagem

- Formato quadrado: `aspect-ratio: 1 / 1`.
- Raio de 8 px; `overflow: hidden`.
- Fundo glass-bg.
- Imagem `object-fit: contain`, preservando todo o enquadramento.
- `loading="lazy"` e `decoding="async"`.
- Cursor zoom-in na ação que abre o visualizador.
- Contorno de repouso: inset de 1 px glass-bg.
- Contorno de hover/foco: inset de 1 px glass-active.
- Zoom de imagem no hover/foco: escala 1.03, duração 600 ms.
- Fallback de imagem: Image de 28 px, mensagem curta, texto muted.

**Distinção importante:** a miniatura da imagem selecionada dentro da barra compacta usa `object-fit: cover` e mede 48 × 48 px; a galeria principal usa `contain`.

### 11.3 Badge de similaridade

- Posição: canto superior direito, 12 px de cada borda.
- Padding `4px 8px`, raio pill.
- Fundo preto a 60%, blur 20 px.
- Contorno branco a 10%.
- Texto branco, 11 px, peso 600, tracking 0.05em.
- Fica visível mesmo sem hover.

O novo app pode substituir o percentual por um estado, categoria ou valor relevante. Usar um badge somente quando ele comunicar algo útil.

### 11.4 Overlay e ações

```css
background: linear-gradient(
  to top,
  rgba(0,0,0,0.85) 0%,
  rgba(0,0,0,0.4) 50%,
  rgba(0,0,0,0) 100%
);
```

- Overlay cobre o card, com conteúdo alinhado embaixo e padding de 16 px.
- Nome: 13 px, peso 500, branco, ellipsis.
- Caminho/metadado: 11 px, branco a 70%, ellipsis.
- Ações circulares de 32 × 32 px; ícones de linha, brancos.
- Ações usam preto a 40%, blur 4 px, contorno branco a 20%.
- No hover, fundo das ações passa a branco a 20%.
- Overlay aparece em hover, focus-visible ou focus-within; transição de opacidade/visibility 300 ms.
- Em repouso, overlay tem `pointer-events: none`.
- A ação de abrir a imagem e os botões adicionais são controles irmãos; evitar botões aninhados.

**Recomendação para touch:** manter ações acessíveis sem depender de hover, por exemplo por seleção do card ou um botão de opções persistente.

## 12. Filtros, seletores e controles pequenos

### 12.1 Filtros

- Botão pill com Filter de 16 px.
- Padding `8px 16px`, texto 13 px / 600 / uppercase / tracking 0.05em.
- Estado ativo com mais luminosidade e badge circular de contagem 20 × 20 px.
- Popover ancorado à direita, 8 px abaixo do botão.
- Largura 280 px, altura máxima 400 px, scroll vertical.
- Fundo surface, blur 20 px, raio 12 px, padding 16 px.
- Contorno glass-border e sombra de popover.
- Títulos de grupos: 11 px / 600 / uppercase / 0.15em / muted.
- Itens: 13 px, gap de 8 px entre checkbox e texto.
- Checkboxes de 16 × 16 px, `accent-color: var(--ds-primary)`.
- Separador: 1 px glass-border.
- Um clique fora fecha o popover; botão informa `aria-expanded`.

### 12.2 Seletor de tamanho das miniaturas

- Image de 14 px em muted.
- Range horizontal de 120 px de largura e 4 px de altura.
- Trilho glass-border.
- Thumb circular de 12 px em primary, escala 1.2 no hover.
- Gap de 12 px entre ícone e range.
- Valor acessível por rótulo e informação textual/tooltip.

### 12.3 Switch

- Trilho de 48 × 26 px, raio 13 px.
- Estado desligado: glass-border.
- Estado ligado: primary.
- Thumb de 22 × 22 px, posicionado 2 px do topo.
- Thumb desligado em muted; ligado em base-black.
- Posição esquerda 2 px → 24 px.
- Transição de 200 ms com `cubic-bezier(0.4, 0, 0.2, 1)`.
- Semântica `role="switch"`, `aria-checked` e rótulo descritivo.

### 12.4 Tabs e controle segmentado

- Grupo em pill, padding de 4 px, fundo neutro/vidro.
- Opções com padding `10px 24px` para tabs ou `8px 16px` para tema.
- Texto 12 px / 600, tabs uppercase e tracking 0.05em.
- Selecionado: primary com texto base-black.
- Não selecionado: transparente com texto muted.
- Hover do não selecionado: glass-active.
- Usar `aria-selected` em tabs e `aria-pressed` em botões de seleção.

## 13. Configurações, listas e painéis

### 13.1 Configurações

A página mantém o mesmo container central do restante do app, com título à esquerda e tabs compactas à direita.

- Seções com títulos 14 px / 600 / uppercase / tracking 0.15em / muted.
- Painéis de vidro com raio de 24 px e contorno glass-border.
- Padding de 24 px nas linhas de configuração.
- Nome da opção: 16 px / 500 / primary.
- Descrição: 14 px / muted, distância de 4–8 px abaixo do nome.
- Controles à direita; em telas menores, reorganizar abaixo da descrição.
- Grupos separados por linhas sutis de 1 px.
- Ações secundárias pill com fundo vidro e contorno fino.

### 13.2 Lista de pastas como padrão de lista de recursos

Esse desenho pode ser reaproveitado para projetos, documentos, coleções, trabalhos ou tarefas:

- Card glass-panel com raio 16 px, padding `20px 24px`, gap vertical 14 px.
- No hover, a borda fica glass-active.
- Identificador visual à esquerda: círculo 42 × 42 px, ícone de 20 px, contorno fino.
- Nome: 15 px / 500 / primary.
- Caminho ou subtítulo: 11 px / 600 / muted / tracking 0.05em.
- Ações de ícone à direita: padding 8 px, raio 8 px, fundo transparente em repouso.
- Hover de ação: ícone primary, vidro e contorno glass-border.
- Desabilitado: opacidade 0.4, cursor not-allowed.
- Toolbar da seção usa botões pill, texto 12 px, ícones de aproximadamente 15 px.
- Nomes longos não devem empurrar os botões para fora: `min-width: 0`, ellipsis e `flex-shrink: 0` na toolbar.

### 13.3 Plano e uso

- Card principal com raio 24 px, padding 24 px, gap 16 px.
- Label técnico de 12 px uppercase e nome do plano de 24 px / 500.
- Barra de uso com 8 px de altura, raio 4 px e trilho glass-border.
- Preenchimento primary; ao atingir limite, error.
- Ofertas secundárias em linhas de vidro com raio 16 px e padding `16px 20px`.

## 14. Login e formulários

### 14.1 Card de login — implementado

- Centralizado em canvas base-black.
- Largura 100%, máximo 420 px.
- Padding `40px 36px`, raio 24 px, gap 20 px.
- Vidro padrão com borda explícita glass-border.
- Título central: 28 px / 500 / tracking -0.02em.
- Subtítulo central: 14 px / muted / uppercase / 0.05em.
- Labels: 12 px / 600 / muted / uppercase / 0.15em.
- Campos: padding `14px 18px`, raio 14 px, fonte 15 px, vidro e borda fina.
- Botão principal ocupa a largura, padding 16 px, raio pill, 14 px / 600 / tracking 0.15em.
- Links auxiliares: 13 px, sublinhados, discretos.
- Mensagens de status e erro dentro do card, com padding 16 px e raio 16 px.

### 14.2 Formulários gerais — recomendação para a família

- Preservar a relação label pequena + campo legível + descrição muted.
- Associar `label` e campo por `htmlFor`/`id`.
- Erro aparece perto do campo ou grupo afetado, com texto e ícone.
- Manter o valor preenchido após falha.
- Distinguir ação primária, secundária e destrutiva.
- Usar `textarea` com raio 12 px para descrições longas.
- Para formulários muito densos, admitir raio 8–12 px nos campos mantendo os demais tokens.

## 15. Modais e visualizador

### 15.1 Modal de trabalho

Há variações locais no app:

| Modal | Backdrop | Painel |
| --- | --- | --- |
| Atualização | Preto a 70%, blur 20 px. | Largura 420 px, padding 32 px, raio 12 px, surface-bright. |
| Servidor no login | Preto a 75%, blur 6 px. | Máximo 480 px, padding 32 px, raio 20 px, surface. |

**Recomendação de padrão para o novo produto:** modal central com largura máxima de 480 px, padding 32 px, raio 20 px, superfície neutra e backdrop preto a 70% com blur de 20 px. É uma consolidação das receitas existentes, não uma medida única já usada por todo modal do desktop.

Títulos de 18–20 px, ícone de 20–24 px, corpo 13–14 px e ações finais com gap de 12 px.

### 15.2 Lightbox — implementado

- Overlay fullscreen, `position: fixed; inset: 0`.
- Ambiente permanentemente escuro: `rgba(10,10,10,0.95)`.
- Blur 40 px, z-index 9999.
- Entrada por fade 300 ms.
- Barra superior com padding `24px 32px`, nome de arquivo e badge.
- Fechar: botão circular 44 × 44 px, X de 20 px.
- Navegação anterior/próxima: círculos 56 × 56 px, ícones Chevron de 28 px, distância lateral 40 px.
- Imagem com `object-fit: contain`, largura máxima 85%, altura máxima 85vh, raio 16 px.
- A imagem cacheada aparece antes da prévia de alta resolução.
- Indicador de carregamento localizado em pill perto da borda inferior.
- Painel de propriedades: largura 320 px, padding `32px 24px`, gap de 24 px, blur 20 px, fundo `rgba(10,10,10,0.4)` e borda esquerda branca a 5%.
- Propriedades em duas colunas com gap de 16 px; labels pequenas uppercase e valores legíveis.
- Amostras de cores do conteúdo: 28 × 28 px, raio 8 px. Essas cores representam a imagem; não são acentos de interface.
- Escape fecha; setas navegam; zoom de 1× a 8× e arraste quando ampliado.

**Recomendação:** modais do novo app devem gerenciar foco, Escape e retorno de foco ao elemento que abriu a sobreposição. O suporte parcial atual não é uma especificação completa de acessibilidade de diálogos.

## 16. Ícones e identidade de marca

### 16.1 Biblioteca

**Implementado:** `lucide-react`. SVGs de linha, sem preenchimento por padrão, `currentColor`, traços com pontas e junções arredondadas.

O código atual não define `strokeWidth` nos componentes Lucide. A versão instalada da biblioteca usa **strokeWidth 2** no viewBox de 24 × 24. Para reproduzir o desktop, usar `strokeWidth={2}` ou o padrão equivalente da biblioteca. O documento antigo recomendava 1.5; esse valor não descreve a renderização atual.

### 16.2 Escala de ícones

| Tamanho | Uso |
| --- | --- |
| 14–16 px | Status, badges, botões compactos, filtros e metadados. |
| 20 px | Navegação, busca, ações gerais, fechar modal. |
| 24 px | Marca e ícones de destaque em painéis. |
| 28 px | Upload, navegação do lightbox, fallback de imagem. |
| 32 px | Estado vazio e splash. |

Usar uma única biblioteca em todo o novo frontend. Cor active = primary; inactive = muted; erro/aviso = tokens semânticos. Os ícones sobre mídia permanecem brancos.

### 16.3 Dicionário de ícones usados

| Função | Ícone Lucide |
| --- | --- |
| Marca do DeepSearch | `Camera` |
| Busca | `Search` |
| Configurações | `Settings` |
| Imagem / miniatura | `Image` |
| Envio / dropzone | `UploadCloud` |
| Filtrar | `Filter` |
| Sem resultados | `SearchX` |
| Busca por similaridade | `ScanSearch` |
| Pasta | `Folder` |
| Abrir pasta | `FolderOpen` |
| Adicionar | `Plus` |
| Excluir | `Trash2` |
| Pausar / retomar | `Pause` / `Play` |
| Sincronizar / atualizar | `RefreshCw` |
| Refazer / reindexar | `RotateCcw` |
| Tempo / velocidade | `Clock` / `Zap` |
| Processando | `Loader2` |
| Concluído | `Check` / `CheckCircle2` |
| Erro | `AlertCircle` / `XCircle` |
| Aviso | `AlertTriangle` |
| Fechar | `X` |
| Navegar anterior / próximo | `ChevronLeft` / `ChevronRight` |
| Tema claro / escuro / sistema | `Sun` / `Moon` / `Monitor` |
| Servidor / nuvem / máquina | `Server` / `Globe` / `Laptop` |
| Sair | `LogOut` |
| Diagnóstico | `Bug` |
| Link externo | `ExternalLink` |
| Próximo passo | `ArrowRight` |

### 16.4 Marca do novo produto

**Recomendação para a família:** manter a relação símbolo de linha de 24 px + nome de 18 px / 600, gap de 12 px e tracking -0.02em. Selecionar um símbolo Lucide que represente a função do novo produto; a câmera é a marca do DeepSearch e não precisa ser o ícone de todos os apps.

O splash do DeepSearch desenha uma câmera SVG branca com `stroke-width="2"`. Essa é uma referência concreta de marca.

**Estado dos assets:** os PNGs `icon.png`, `128x128.png` e `32x32.png` do bundle foram inspecionados e estão completamente transparentes nesta cópia do projeto. Não utilizá-los como referência gráfica pronta. Para o novo app, exportar um ícone real com o símbolo escolhido e validar sua aparência nas dimensões de distribuição.

## 17. Movimento e microinterações

### 17.1 Tempos e curvas observados

| Interação | Duração | Curva |
| --- | --- | --- |
| Cor de navegação | 200 ms | CSS padrão/ease. |
| Botões e controles | 200 ms | Ease ou `cubic-bezier(0.16,1,0.3,1)`. |
| Thumb do range | 100 ms | Transição curta. |
| Progresso no rodapé | 300 ms | Ease. |
| Overlay de card | 300 ms | Ease. |
| Contorno de card | 400 ms | `cubic-bezier(0.16,1,0.3,1)`. |
| Zoom da miniatura | 600 ms | `cubic-bezier(0.16,1,0.3,1)`. |
| Entrada de modal de atualização | 300 ms | Mesma curva suave. |
| Entrada da imagem no lightbox | 500 ms | Mesma curva suave. |
| Spinner | 1 s | Linear, infinito. |
| Skeleton da galeria | 2 s | Linear, infinito. |
| Skeleton de propriedades | 1.5 s | Linear, infinito. |
| Status de varredura | 1.2 s | Pulso de escala/opacidade. |
| Status de indexação | 1.8 s | Pulso mais suave. |

### 17.2 Princípios

- Hover aumenta luminosidade e, em poucos controles, escala discretamente.
- Evitar deslocar vizinhos durante hover.
- Reservar transformações maiores para abrir detalhes.
- A aparência do conteúdo pronto não depende de terminar uma animação longa.
- Para a família, preferir transições explícitas de `opacity`, `transform`, `background-color`, `color` e `border-color` a `transition: all`.
- Priorizar `opacity` e `transform` em animações de rolagem; preservar a altura da composição.

### 17.3 Movimento reduzido

O desktop já adapta o recolhimento da busca para fade sem deslocamento quando `prefers-reduced-motion: reduce` está ativo. Essa adaptação não cobre automaticamente todos os spinners, skeletons e pulsos do app.

**Recomendação para a família:** estender a preferência a todos os movimentos decorativos; retirar shimmer, pulso e zoom de hover, mantendo mensagens textuais de operação. A barra compacta deve continuar acessível mesmo quando a transformação for removida.

## 18. Estados de interface

| Estado | Tratamento visual e comportamento |
| --- | --- |
| Repouso | Superfícies discretas, ações disponíveis, texto secundário muted. |
| Hover | Luminosidade e borda aumentam; cor de ação vai para primary. |
| Foco | Outline de 2 px em primary, normalmente offset de 2 px. |
| Selecionado | Contraste invertido, especialmente em tabs/tema. |
| Carregando | Spinner local, rótulo de ação em andamento ou skeleton no espaço do conteúdo. |
| Vazio | Ícone neutro, título claro e próxima ação sugerida. |
| Erro | Ícone + mensagem + tokens error; preservar o contexto da tela. |
| Aviso / pausado | Tokens warning e texto explicativo. |
| Desabilitado | Cursor not-allowed e menor opacidade/contraste. |
| Concluído | Check neutro e informação textual; cor não é o único sinal. |

### 18.1 Skeleton da galeria

- 12 placeholders quadrados no mesmo grid dos resultados.
- Raio 8 px, fundo glass-bg.
- Gradiente `glass-bg 25% → glass-border 50% → glass-bg 75%`.
- `background-size: 200% 100%` e shimmer de 2 s.
- Usar skeleton apenas quando realmente aguardando conteúdo.

### 18.2 Estado sem resultados

- Padding `80px 20px`, conteúdo centralizado.
- Círculo de 80 × 80 px com SearchX de 32 px, em muted.
- Gap de 24 px até o título.
- Título 18 px / 500 / primary; explicação 14 px / muted.

### 18.3 Progresso e rodapé

- Rodapé combina contagem à esquerda e situação à direita.
- Ação de pausa/retomada com ícone de 14 px.
- Barra fina de 100 × 2 px, trilho glass-border e preenchimento primary.
- Estados de pastas usam ponto de 7 px: primary para pronto/processando, muted para fila e warning para pausa.
- Estado precisa ser entendível pelo texto, independentemente da animação ou cor do ponto.

## 19. Splash e inicialização

**Implementado:** splash separado de 340 × 340 px, sem decoração de janela, conteúdo centralizado.

- Fundo preto.
- Badge de 80 × 80 px, raio 24 px, branco a 2%, contorno branco a 8%.
- Câmera branca de 32 px.
- Nome uppercase, 20 px / 600 / tracking 0.2em, distância superior 32 px.
- Trilho de carregamento 140 × 3 px, raio pill, distância superior 28 px.
- Preenchimento em gradiente branco de 30% até 100%.
- Badge pulsa em 2 s; título entra por fade e deslocamento de 10 px em 1 s.

A barra do splash usa uma animação temporal de 5 s; não representa uma medição real de percentual da inicialização.

**Recomendação:** para o novo produto, manter a composição de marca se houver splash, mas mostrar estado indeterminado ou progresso real. O frontend deve aparecer assim que estiver funcional, sem uma espera decorativa obrigatória.

## 20. Tema claro, escuro e sistema

**Implementado:** opções `light`, `dark`, `system`, persistidas em `localStorage` com chave `ds-theme`.

- Tema aplicado em `document.documentElement.dataset.theme`.
- Sistema usa `matchMedia('(prefers-color-scheme: dark)')` e acompanha mudanças.
- CSS define tokens do tema escuro como base e sobrescreve no tema claro.
- Seletor segmentado com Sun, Moon e Monitor.

**Recomendação para o novo app:** manter os mesmos tokens e opções. Aplicar a preferência antes da primeira pintura quando possível para reduzir flash de tema. A persistência pode usar uma chave própria; a preferência entre aplicativos só é compartilhada se houver uma estratégia explícita para isso.

O lightbox permanece escuro nos dois temas. Outras áreas do produto devem trocar os tokens, e não manter fundos pretos ou texto branco fixos por acidente.

## 21. Responsividade e acessibilidade para a família

O desktop tem containers fluidos, grid adaptável e alguns grupos com `flex-wrap`, mas não possui um conjunto completo de breakpoints mobile. As medidas abaixo são **recomendações para o novo frontend**.

| Largura | Adaptação recomendada |
| --- | --- |
| Até 599 px | Margens 16 px; busca 100%; controles em linhas quebráveis; propriedades abaixo do conteúdo; rodapé simplificado. |
| 600–899 px | Margens 24 px; busca até 90%; painéis podem reorganizar controles abaixo dos textos. |
| A partir de 900 px | Composição próxima ao desktop: cabeçalho flutuante, área 90%/1400 px, busca 70%. |
| Telas amplas | Manter o limite de 1400 px para trabalho padrão e preservar o respiro lateral. |

### Regras de adaptação

- Em viewport estreito, limitar o mínimo do card à largura disponível: `minmax(min(100%, var(--thumb-size)), 1fr)`.
- No mobile, mover ou recolher o painel de 320 px do lightbox; não esmagar a área principal.
- Permitir `flex-wrap` nas toolbars, tabs e controles que não cabem em uma linha.
- Usar `100dvh` com fallback quando o shell for web e precisar acompanhar barras móveis do navegador.
- Hover não pode ser a única maneira de descobrir uma ação no touch.
- Área de toque recomendada de pelo menos 44 × 44 px, mesmo quando o glifo mede 14–20 px. Os botões de 32 px sobre imagem do desktop precisam dessa adaptação em touch.
- Campos com fonte de pelo menos 16 px em mobile quando necessário para evitar zoom automático do navegador.

### Regras de acessibilidade

- Contraste de texto normal: verificar pelo menos 4.5:1; texto grande: 3:1; controles essenciais e foco: 3:1.
- Conferir a cor final composta quando houver vidro sobre fotografia; não avaliar somente o RGBA isolado.
- Toda ação por ícone tem nome acessível e, se útil, tooltip.
- Usar botões reais e formulários semânticos.
- Erros anunciados por `role="alert"`; andamento/sucesso por `role="status"` quando apropriado.
- Tabs, switches, ranges, popovers e progresso têm estado acessível.
- Em modal: título associado, foco inicial, contenção de foco e retorno ao fechar.
- Foco permanece visível mesmo em componentes que têm `outline: none` no estilo de repouso.
- Todos os textos da interface em inglês americano (`en-US`), incluindo botões, erros, acessibilidade, datas e números. A moeda dos planos continua BRL, com formatação americana.

## 22. Técnicas utilizadas e como transportá-las

### Implementação atual

| Técnica | Onde aparece / finalidade |
| --- | --- |
| React + TypeScript | Componentes, estado, props tipadas e composição de telas. |
| Vite | Desenvolvimento e build do frontend. |
| Tauri | Janela desktop, comandos nativos, eventos e acesso autorizado a arquivos. |
| Lucide React | Ícones SVG consistentes. |
| CSS custom properties | Paleta e troca de tema. |
| Estilos inline React | Grande parte das receitas específicas dos componentes. |
| CSS global | Reset, vidro, estados de cards, slider e animações de busca. |
| CSS Modules | Controles de configurações. |
| Flexbox | Shell, toolbars, formulários e linhas de configurações. |
| CSS Grid | Galeria, propriedades e sobreposição das duas barras de busca. |
| Backdrop filter | Vidro, badges e sobreposições. |
| IntersectionObserver + scroll timeline CSS | Recolhimento da barra de busca. |
| Lazy loading e decoding async | Carregamento das miniaturas. |
| Cache seguido de detalhe | Miniatura imediata e preview de alta resolução posterior. |
| Ellipsis e min-width: 0 | Nomes e caminhos sem quebrar o layout. |
| Media query de sistema | Tema e preferência de movimento reduzido. |

### Portabilidade

O design não exige Tauri, nem comandos nativos, nem Tailwind. Outro app pode usar React, Vue, Svelte ou HTML/CSS desde que respeite as receitas. Se usar Tailwind ou uma biblioteca de componentes, ajustar os tokens e componentes ao guia; os defaults visuais da biblioteca não são a identidade do conjunto.

**Recomendação:** manter tokens em um stylesheet central e receitas em classes/componentes reutilizáveis. Isso facilita compartilhar a identidade sem copiar o backend, as APIs ou a lógica de negócio do DeepSearch.

## 23. CSS inicial pronto para copiar

As cores e a fonte abaixo correspondem ao desktop. As classes de componente são uma base portátil que consolida suas receitas; não são uma cópia integral do frontend.

```css
:root,
:root[data-theme="dark"] {
  --ds-font: system-ui, -apple-system, "Segoe UI", Roboto,
    "Helvetica Neue", Arial, sans-serif;
  --ds-base-black: #000000;
  --ds-obsidian: #050505;
  --ds-surface: #131313;
  --ds-surface-dim: #0e0e0e;
  --ds-surface-bright: #2a2a2a;
  --ds-primary: #ffffff;
  --ds-muted: #888888;
  --ds-glass-bg: rgba(255, 255, 255, 0.02);
  --ds-glass-border: rgba(255, 255, 255, 0.08);
  --ds-glass-active: rgba(255, 255, 255, 0.15);
  --ds-error: #ffb4ab;
  --ds-error-bg: rgba(255, 180, 171, 0.1);
  --ds-error-border: rgba(255, 180, 171, 0.3);
  --ds-warning: #ffdc9c;
  --ds-warning-bg: rgba(255, 170, 50, 0.1);
  --ds-warning-border: rgba(255, 170, 50, 0.3);
  color-scheme: dark;
}

:root[data-theme="light"] {
  --ds-base-black: #f8fafc;
  --ds-obsidian: #ffffff;
  --ds-surface: #f1f5f9;
  --ds-surface-dim: #e2e8f0;
  --ds-surface-bright: #ffffff;
  --ds-primary: #0f172a;
  --ds-muted: #475569;
  --ds-glass-bg: rgba(0, 0, 0, 0.03);
  --ds-glass-border: rgba(0, 0, 0, 0.08);
  --ds-glass-active: rgba(0, 0, 0, 0.15);
  --ds-error: #ba1a1a;
  --ds-error-bg: rgba(186, 26, 26, 0.1);
  --ds-error-border: rgba(186, 26, 26, 0.3);
  --ds-warning: #995500;
  --ds-warning-bg: rgba(230, 120, 0, 0.1);
  --ds-warning-border: rgba(230, 120, 0, 0.3);
  color-scheme: light;
}

* { box-sizing: border-box; }
html, body { margin: 0; min-height: 100%; }
body {
  background: var(--ds-base-black);
  color: var(--ds-primary);
  font-family: var(--ds-font);
  -webkit-font-smoothing: antialiased;
}
button, input, textarea, select { font: inherit; }
button { cursor: pointer; }
button:disabled { cursor: not-allowed; opacity: 0.7; }

.ds-glass {
  background-color: var(--ds-glass-bg);
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  box-shadow: inset 0 0 0 1px var(--ds-glass-border);
}
.ds-content {
  width: 90%;
  max-width: 1400px;
  margin-inline: auto;
  padding-block: 40px;
}
.ds-panel { border-radius: 24px; padding: 24px; }
.ds-title {
  margin: 0;
  font-size: 24px;
  font-weight: 500;
  letter-spacing: -0.02em;
}
.ds-label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.15em;
  text-transform: uppercase;
  color: var(--ds-muted);
}
.ds-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 12px 24px;
  border: 1px solid var(--ds-glass-border);
  border-radius: 100px;
  background: var(--ds-glass-bg);
  color: var(--ds-primary);
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  transition: background-color 200ms ease, opacity 200ms ease;
}
.ds-button:hover:not(:disabled) { background: var(--ds-glass-border); }
.ds-button-primary {
  border-color: transparent;
  background: var(--ds-primary);
  color: var(--ds-base-black);
}
.ds-button-primary:hover:not(:disabled) {
  background: var(--ds-primary);
  opacity: 0.9;
}
.ds-button-danger {
  border-color: var(--ds-error-border);
  background: var(--ds-error-bg);
  color: var(--ds-error);
}
.ds-button-danger:hover:not(:disabled) { background: var(--ds-error-border); }
.ds-input {
  width: 100%;
  padding: 14px 18px;
  border: 1px solid var(--ds-glass-border);
  border-radius: 14px;
  background: var(--ds-glass-bg);
  color: var(--ds-primary);
  font-size: 15px;
  transition: border-color 200ms ease;
}
.ds-input::placeholder { color: var(--ds-muted); }
.ds-input:focus { border-color: var(--ds-primary); }
.ds-input[aria-invalid="true"] { border-color: var(--ds-error-border); }
.ds-alert {
  padding: 16px;
  border: 1px solid var(--ds-error-border);
  border-radius: 16px;
  background: var(--ds-error-bg);
  color: var(--ds-error);
  font-size: 13px;
  line-height: 1.4;
}
:where(button, input, textarea, select, [tabindex]):focus-visible {
  outline: 2px solid var(--ds-primary);
  outline-offset: 2px;
}
```

**Receita de painel:** combinar `.ds-glass.ds-panel`. **Receita de ação principal:** combinar `.ds-button.ds-button-primary`. A composição explícita evita que uma classe genérica mude acidentalmente a cor de outra variante.

`color-scheme` e a classe `.ds-input::placeholder` são ajustes portáteis recomendados; o desktop não declara essas duas regras globais exatamente assim.

### Adaptação responsiva opcional

```css
/* Recomendação para um frontend web da família. */
.ds-media-grid {
  --thumb-size: 240px;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, var(--thumb-size)), 1fr));
  gap: 24px;
}
@media (max-width: 599px) {
  .ds-content { width: auto; margin-inline: 16px; padding-block: 24px; }
  .ds-panel { padding: 20px; }
  .ds-media-grid { gap: 16px; }
  .ds-input { font-size: 16px; }
}
```

## 24. O que permanece igual entre os aplicativos

| Preservar na família | Pode variar conforme o produto |
| --- | --- |
| Paleta e semântica dos tokens. | Nome e símbolo da marca. |
| Fonte, pesos e tracking. | Conteúdo e ordem das telas. |
| Vidro, bordas e comportamento de hover. | Galeria, editor, lista, tabela ou dashboard. |
| Idioma visual de botões, campos, tabs e switches. | Quantidade e organização dos destinos de navegação. |
| Biblioteca e escala dos ícones. | Ícones específicos do domínio. |
| Raio conforme categoria de componente. | Dimensões excepcionais exigidas por uma ferramenta. |
| Feedback de erro, carregamento e vazio. | Mensagens e rótulos de negócio. |
| Tema claro/escuro/sistema. | Integrações nativas ou web. |

A semelhança deve vir do material e dos controles compartilhados, sem obrigar outro produto a copiar uma tela de busca de imagens que não faça sentido para ele.

## 25. Limites visuais e critérios de aprovação

### Verificar na entrega do novo frontend

- [ ] Fundo escuro `#000000`, superfícies neutras e destaque principal branco.
- [ ] Tema claro usa todos os tokens correspondentes, com contraste invertido nas ações.
- [ ] Fonte igual à família; sem dependência remota inesperada de fonte.
- [ ] Cabeçalho, containers e ritmo de espaçamento lembram o desktop.
- [ ] Botões principais pill, painéis grandes suaves e cards de mídia com raio 8 px.
- [ ] Glass blur de 20 px com contorno discreto, sem vidro opaco demais.
- [ ] Ícones Lucide consistentes, strokeWidth 2, tamanhos adequados.
- [ ] Conteúdo de imagens preservado por contain quando a intenção for mostrar a imagem inteira.
- [ ] Cores saturadas reservadas a conteúdo e estados semânticos necessários.
- [ ] Hover não altera a geometria da página e foco de teclado é visível.
- [ ] Overlay de card também funciona por teclado e touch.
- [ ] Estado vazio, carregando, erro, desabilitado e concluído desenhados.
- [ ] Modais funcionam por teclado e devolvem foco.
- [ ] Movimento reduzido respeitado em toda a interface.
- [ ] Layout verificado em 375, 768, 1024 e 1440 px, se o produto for web responsivo.
- [ ] Textos da interface em inglês americano (`en-US`).
- [ ] Apenas ações reais do produto são exibidas; controles não são decoração.

### Desvios que mudam a identidade

Evitar acentos azuis/laranja de templates genéricos, gradientes coloridos decorativos, fontes de display marcantes, sombras em todos os cards, outlines pesados, ícones de famílias misturadas e dashboards cheios de cards sem necessidade funcional. Esses recursos devem ser uma decisão explícita de evolução da família, não um resultado automático da geração.

## 26. Prompt pronto para entregar à IA

```text
Você vai construir o frontend de [NOME DO NOVO APP], um produto da mesma
família visual do DeepSearch Cloud. Use o design.md fornecido como
especificação de identidade. As funcionalidades e telas são: [DESCRIÇÃO].

Preserve a linguagem Polished Obsidian / Liquid Light: paleta monocromática,
fundo escuro #000000, primary #FFFFFF, muted #888888, superfícies neutras,
vidro discreto com blur 20px e contorno branco a 8%. Implemente também
o tema claro com os tokens exatos do documento e a opção de tema do sistema.

Use a fonte de sistema definida no guia, títulos de página 24px/500 com
tracking -0.02em, labels técnicos compactos uppercase e ações pill.
Use Lucide com strokeWidth 2, escala de ícones 14–32px e nomes acessíveis.
Preserve os raios por categoria: mídia 8px, listas 16px, painéis 24px.

Adapte a estrutura às funcionalidades do novo app. Mantenha os materiais,
controles, estados e espaçamentos compartilhados. Troque a marca por
[NOME/SÍMBOLO DO NOVO APP]. Se não houver busca de imagens, use os mesmos
padrões para listas, editor, documentos, tabelas ou dashboard.

Centralize tokens e receitas reutilizáveis. Diferencie implementações
observadas das adaptações recomendadas no guia. Não copie documentos
antigos do repositório que conflitem com esta especificação.

Entregue estados de carregamento, vazio, erro, seleção, foco e desabilitado;
suporte teclado, touch, movimento reduzido e os tamanhos de tela exigidos.
Use dados de exemplo coerentes com o domínio, sem mostrar detalhes de
infraestrutura ao usuário quando não fizerem parte da função da tela.

Ao finalizar, confira a lista de aprovação do design.md e descreva apenas
eventuais decisões visuais que precisaram divergir da referência.
```

## 27. Apêndice: variante do painel administrativo

O painel web em `admin/` pertence ao projeto, mas tem uma execução visual mais densa e operacional. Esta variante é adequada quando o novo produto exigir muitas tabelas e ferramentas administrativas. O desktop continua sendo a referência principal deste guia.

### Tokens próprios do admin

| Token | Valor |
| --- | --- |
| surface | `#131313` |
| surface-low | `#1c1b1b` |
| surface-container | `#201f1f` |
| surface-high | `#2a2a2a` |
| on-surface | `#e5e2e1` |
| on-surface-variant | `#c4c7c8` |
| outline | `#8e9192` |
| outline-variant | `#444748` |
| primary | `#ffffff` |
| on-primary | `#2f3131` |
| error | `#ffb4ab` |
| error-container | `#93000a` |
| ok | `#9ed8a6` |
| warn | `#f2c46f` |

- Font stack declarado: Inter, system-ui, -apple-system, Segoe UI, sans-serif. O CSS não carrega uma fonte Inter nesse arquivo; o nome não garante sua disponibilidade.
- Fonte base de 14 px; título de página 22 px / 600; seção 15 px / 600.
- Sidebar de 210 px, padding `16px 12px`, fundo surface-low e contorno outline-variant.
- Conteúdo com padding `24px 32px`.
- Cards de métrica com raio 8 px, padding `12px 14px`, números 18 px / 600.
- Grade de métricas `auto-fill/minmax(170px,1fr)`, gap 12 px.
- Campos e botões mais compactos: raio 6 px, padding `6px 10px`.
- Tabelas com padding `8px 10px` por célula, linhas de 1 px outline-variant.
- Labels de tabela 12 px; JSON/identificadores em fonte monoespaçada quando apropriado.
- Badges pill com fonte 11 px, contorno currentColor e cores semânticas.
- O admin atual define tema escuro; não possui a mesma implementação de troca de tema do desktop.

**Recomendação:** se precisar dessa densidade no novo produto, aplicar receitas de tabela e métricas à paleta principal da família. Não misturar automaticamente os dois conjuntos de tokens em uma mesma tela.

## 28. Origem das informações e diferenças de documentos anteriores

Os caminhos abaixo servem para manutenção deste guia. A IA que receber apenas este arquivo não precisa consultá-los.

| Fonte do projeto | Informação principal |
| --- | --- |
| `desktop/src/main.tsx` | Tokens reais, reset, vidro, foco, hover de cards e CSS de rolagem. |
| `desktop/src/App.tsx` | Shell, marca, navegação, rodapé e modal de atualização. |
| `desktop/src/hooks/useTheme.tsx` | Tema light/dark/system e persistência. |
| `desktop/src/components/SettingsWindow.module.css` | Tabs, tema, switches e botões. |
| `desktop/src/components/search/SearchView.tsx` | Busca completa/compacta, layout e toolbar. |
| `desktop/src/components/search/DropZone.tsx` | Upload, drag e foco. |
| `desktop/src/components/search/ResultGrid.tsx` | Grid, skeleton e vazio. |
| `desktop/src/components/search/ResultCard.tsx` | Miniaturas, overlay, badge e ações. |
| `desktop/src/components/search/FilterPopover.tsx` | Filtros e popover. |
| `desktop/src/components/search/ThumbSizeSlider.tsx` | Range de tamanho. |
| `desktop/src/components/search/Lightbox.tsx` | Visualizador, propriedades e movimento. |
| `desktop/src/components/LoginForm.tsx` | Login, campos e modal de servidor. |
| `desktop/src/components/SettingsWindow.tsx` | Painéis e organização das configurações. |
| `desktop/src/components/FolderList.tsx` | Listas de recursos, toolbars e status. |
| `desktop/src/components/PlanPanel.tsx` | Plano, uso e mensagens. |
| `desktop/public/splashscreen.html` | Splash e câmera SVG. |
| `desktop/src-tauri/tauri.conf.json` | Tamanhos de janela e recursos de ícone. |
| `admin/src/styles.css` | Variante administrativa. |
| `desktop/node_modules/lucide-react/dist/cjs/lucide-react.js` | Stroke padrão 2 da versão instalada. |

### Divergências verificadas

- `desktop/DESIGN.md` descreve a intenção Polished Obsidian, mas parte de seus tokens, raios, grid e fonte não acompanha o código atual. Este guia prioriza os componentes implementados.
- A referência antiga cita Inter; o runtime usa a pilha de sistema.
- A referência antiga cita ícones de 1.5; a execução atual usa Lucide no padrão 2.
- A referência antiga cita grid de 12 colunas e imagens full-bleed; a galeria atual usa auto-fill, cards quadrados e object-fit contain.
- A referência antiga sugere raio geral de 4 px; o desktop atual usa pills e painéis grandes de 24 px, além de raios por componente.
- `desktop/design_system.md` contém uma proposta de marketplace com azul e laranja. Essa proposta não corresponde à identidade visual atual do desktop e não deve orientar a geração do app irmão.

Para evoluir a família, atualizar este documento junto com as receitas compartilhadas. Alterações de paleta, fonte ou material devem ser tratadas como mudanças do conjunto, para preservar a relação entre os produtos.
