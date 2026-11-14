# Auditoria técnica — Pipeline de modernização gráfica PT Asset Tool + Etervale + Blender

**Data:** 09/11/2026

**Escopo:** cópia de código do Etervale (`Etervale-main(2).zip`), cópia de trabalho do PT Asset Tool com patches P0/P1, logs reais de validação fornecidos durante a auditoria e requisitos do documento `Projeto: Pipeline de Modernização Gráfica — Priston Tale + Blender`.

**Tipo de prova:** inspeção estática de código + smoke test Python offline + testes unitários de processamento de texturas. **Não é prova de GPU, qualidade visual nem desempenho no jogo.**

**Proveniência:** `Etervale-main(2).zip` foi o arquivo de referência disponível durante aquela auditoria (sem a pasta `assets/`). O caminho `/mnt/data/native_content_work/asset` identificado no relatório original era uma cópia **temporária do PT Asset Tool no ambiente de análise**, não um diretório do repositório Etervale nem da instalação Fedora. Resultados descritos como obtidos "neste ambiente" pertencem à sessão de auditoria de 09/11/2026, não a uma validação posterior da branch publicada.

## Síntese

A arquitetura é **viável**, mas o Etervale não interpreta automaticamente materiais PBR completos produzidos pelo Blender. O cliente **já tem** renderer PBR metálico-rugoso (HLSL Shader Model 6.0 compilado a SPIR-V no MonoGame/Vulkan), IBL, BRDF GGX, reflexos, HDR, SSAO, transparência, normal geométrica e shaders de diagnóstico. A lacuna está **entre autoria e renderização**: os formatos legados não definem mapas modernos; o adaptador atual atribui roughness/metallic fixos por tipo de superfície, e o shader de superfície apenas recebe base color e uma camada adicional legada. Normal maps, ORM/metallic-roughness, height/parallax e AO por material não estão ligados ao modelo PBR.

A estratégia mais segura é **manter `.smd/.smb/.inx` e seus leitores**, fazer a geometria e UV continuarem no caminho atual e acrescentar *sidecar/manifesto de materiais* mais uma extensão opcional do binding de material do cliente. O fallback continua sendo `PristonMaterialAdapter`. Trata-se de código novo no pipeline de materiais/render, **não de alteração dos readers de formatos de asset**. Ainda requer aprovação/ensaios do usuário antes de qualquer alteração no Etervale.

## Matriz de compatibilidade — propriedades de materiais

A coluna `Blender` representa capacidade genérica de autoria, não algo comprovado num run do Blender dentro deste ambiente. `Asset Tool` refere-se ao processamento offline. `SMD/SMB` corresponde ao material legado, não ao GLB exportado para edição.

| Propriedade | Blender | Asset Tool atual | SMD/SMB nativo | Cliente loader e shader do Etervale | Conclusão |
|---|---|---|---|---|---|
| Base Color / Diffuse | Autoria | Lê, converte, upscala | Sim, referência de textura, UV | **Sim**, `BaseColorMap`, sRGB → linear | Caminho estabelecido; testar imagens reais |
| Alpha / Opacity | Autoria | Preservação de alpha com gates | Sim, TGA/opacity e flags | **Sim**, mask/blend/cutoff | Disponível; validar casos protegidos e sprites |
| Emissive | Autoria | Trata texturas; mapa separado não conectado | Flag `SelfIllum`/blend | **Parcial**, emissão do próprio albedo | Mapa emissivo independente exige extensão |
| Roughness | Autoria/mapa | Pode armazenar saída offline, não liga shader | **Não**, sem campo PBR próprio | **Sim escalar**, atribuído por heurística (0,85 etc.) | Shader pronto para escalar; mapa precisa binding |
| Metallic | Autoria/mapa | Pode armazenar saída offline, não liga shader | **Não**, sem campo PBR próprio | **Sim escalar**, adapter usa 0 no legado | Shader pronto para escalar; mapa precisa binding |
| Specular / F0 | Autoria/mapa | Sem integração por material moderno | Shininess/reflexo legado parcial | **Sim BRDF + reflectance escalar**, não specular map | É viável adicionar controles; workflow spec/gloss não direto |
| Glossiness | Autoria/mapa | Sem fluxo nativo integrado | Sem referência padrão | **Não como entrada separada**, derivável aprox. por roughness | Conversão controlada + QA, não equivalência exata |
| Normal map | Autoria e baking | **Sim**: de height, DX/OpenGL, renormalização | Sem contrato nativo para normal map | **Não ligado** à superfície; normal **geométrica** sim | Requer mapa externo, tangente/bitangente e shader |
| Height / Parallax | Autoria | Height estimado ou authored; demo offline | Sem contrato nativo | **Não** no shader de superfícies | Shader de parallax e definição de escala exigidos |
| Displacement real | Autoria | Não demonstrado pipeline de mesh displacement nativo | Não demonstrado roundtrip garantido | Não há caminho demonstrado de displacement por textura | Exige geometria nova/tessellation/vertex path |
| AO por textura | Autoria/baking | Sem ligação ao material | Sem contrato específico | **Não** como mapa do material; **sim** SSAO/oclusão de tela | Não confundir SSAO e AO armazenada em imagem |
| Segunda textura UV | Autoria UV1 | Parsers/writers e exportadores parciais | **Sim**, TexLinks em cadeia | **Sim**, uma camada adicional atualmente consumida | Não equivale a slots PBR arbitrários |

**Importante:** um mapa ORM empacotado em RGB **não** vai funcionar apenas por ser carregado. O shader precisa amostrar/decodificar os canais como oclusão, roughness e metallic, no espaço de cor linear, e o loader deve fornecer a textura correta.

## Inventário técnico das três camadas

### Etervale: renderer, formato e carregador

1. **Backend e shaders**: `README.md` exige Vulkan 1.1+; `docs/rendering.md` (linhas 51–58) documenta fork MonoGame Native Vulkan; `docs/shaders.md` (linhas 79–85) descreve HLSL SM 6.0 → SPIR-V por DXC. Não é correto avaliar o cliente como se ainda fosse o antigo Direct3D fixo.
2. **BRDF/PBR**: `Client/PristonTale/Content/Shaders/Lighting/Brdf.hlsli`, `Materials/MaterialDecl.hlsli`, `Materials/Material.hlsli`, `Passes/Surface.fx`. Os shaders incluem GGX, Smith, Schlick, IBL, roughness/metallic/F0, emissive, alpha e SSAO.
3. **Falta de mapas PBR por material**: `Materials/MaterialDecl.hlsli` define apenas `BaseColorMap` (t0) e `LayerMap` (t1) como texturas do material; outros slots têm função de iluminação da cena. `SampleSurface` usa `Roughness`/`Metallic` **escalares**, e `OrientNormal(normal, visibleFace)` a partir da normal de geometria, não uma normal map por UV.
4. **Conversão do legado**: `Client/PristonTale.Engine3D/Rendering/PristonMaterialAdapter.cs` afirma explicitamente que os materiais antigos não contêm metalness/roughness. Padrões: roughness 0,85, metallic 0, water roughness 0,12, reflexo legado 0,4; não são amostras de mapa PBR.
5. **Layer e UV**: `PristonMaterialRuntimeFactory.cs` carrega no máximo uma textura adicional (`Math.Min(material.CombinedLayerCount, 1)`); `PristonCornerVertex.cs` e `PristonStaticVertex.cs` preservam/transportam UV0, UV1, UV2. A capacidade de UV não garante shader de material PBR.
6. **Texturas**: `PristonTextureLoader.cs` decodifica assets do jogo com `PristonImageDecoder`, compõe opacity se necessário, gera mipmaps (`PristonMipChain`), carrega cena em sRGB e usa branco como fallback. A resolução alternativa por extensão procura **`.bmp`, `.tga`, `.jpg`, `.jpeg`**, não `.png`; por isso saída PNG do Blender não deve ser colocada no diretório do jogo supondo que o loader a encontre.
7. **Referências de material no formato**: `Formats/PristonFormats.cs` (record `PristonMaterial`, `PristonTexLink`) descreve difuso, flags, blend, opacidade, nomes de texturas e UVs encadeadas; não há campos normal/roughness/metallic/height.
8. **GLB é fluxo de saída para trabalho externo**: `tools/PristonTale.GltfExport/MaterialFactory.cs` exporta cor base, opacidade e, opcionalmente, emissive. Configura glTF metallic 0 / roughness 1 por padrão. Isso não demonstra um importador GLB no runtime nem roundtrip completo de PBR para SMD.
9. **Diagnóstico já incorporado**: `PristonFrameContext.cs` expõe `PristonDebugView` para Albedo, Normals, Roughness, Metallic, AmbientOcclusion e Specular; `GameplayScreen.cs` liga `Ctrl+F10` ao ciclo de vistas. `PristonCalibrationSpheres.cs` constrói duas esferas (18% cinza e chrome), renderizadas pelo mesmo pass PBR; `ClientSettings.CalibrationSpheres` habilita esse recurso. Portanto **já existe uma cena de referência nativa de dois materiais**, boa para experimento C.
10. **Testes GPU existentes**: `docs/shaders.md` documenta `--verify-shaders` nas probes C#, mas **não foram executados neste ambiente**.

### PT Asset Tool

1. `app/pt_asset_tool/texture_upscale.py`: upscale 2×/3×/4× com Real-ESRGAN NCNN/Vulkan quando disponível ou Lanczos, staging, checksums, diagnóstico, rollback, proteção de TGA/BMP proprietários, filtragem de texturas técnicas e restrição configurável `--max-dimension` (padrão **8192 no programa**, **não** limite comprovado da GPU).
2. `app/pt_asset_tool/texture_material_pipeline.py` e `texture_material_profiles.py`: perfis conservadores por material, saídas de qualidade, restauro 1×, staging e modelo externo opcional (Spandrel/CLI). O programa diferencia mapa authored de height estimado por luminância e marca mapas técnicos como **offline-only**, sem integração ao shader.
3. `app/pt_asset_tool/texture_material_maps.py`: `height_from_diffuse`, `normals_from_height` (convenções DirectX/OpenGL), `renormalize_normals`, `parallax_demo`. A inferência de height por luminância é hipótese artística a revisar, **não** reconstrução física.
4. `app/pt_asset_tool/blender_workspace.py`, `app/pt_smd_blender_import_v4_generic.py`, `app/pt_blender_to_smd_static_v1.py` e conversores relacionados: há caminhos de trabalho com Blender/SMD, mas não há certificação global do roundtrip com rigging/animações e materiais PBR.
5. `app/pt_asset_tool/model062_material_writer.py`, `stage072_material_writer.py`, `smb_writer.py`, `inx_writer.py` e `etervale_writer_cert.py`: capacidade parcial de escritura nativa. Nas validações **reais do usuário** o AssetDump aceitou Stage e Model **sintéticos**, não se pode promover esse resultado a certificação de meshes complexas.
6. chaiNNer: integração indireta possível por motor externo/Spandrel via `texture_material_pipeline.py`; não foi demonstrada invocação nativa de um workflow chaiNNer com a versão e instalação do usuário.
7. Real-ESRGAN/Blender: o código suporta/integra as ferramentas, mas a disponibilidade dos executáveis e GPU na máquina do usuário continua **não medida** nesta auditoria.

### Corpus de assets reais

**Limitação material:** o ZIP `Etervale-main(2).zip` analisado contém o código do jogo **sem** a pasta `assets/`. Os logs anteriores fornecidos pelo usuário provam leitura de SMD real `Field/Ricarten/village-2.smd` e apontam `tem_wall04.bmp` ausente (material 173, 1 face), mas **não fornecem inventário de resoluções, bit depth, padrões de normal/height, largura de VRAM ou amostra de todos os arquivos**. A ausência de `assets/` no ZIP não significa que não existam na máquina do usuário. O script adicional `etervale_graphics_audit_readonly.py` recolhe inventário na máquina do usuário, sem publicar assets.

As **80 referências não resolvidas** citadas no `WorldImport` são passivo conhecido do baseline e **não aumentaram** após a edição de SPM (80 → 80). Não devem ser apresentadas como regressão ou causa de impedimento da pipeline gráfica; motion/INX naquele teste foram verificados com `--assets` apontado para diretório propositalmente vazio.

## O que é possível sem modificar o renderer

- Upscale/denoise de **texturas difusas** mantendo formato/caminho e perfil adequado, com testes de alpha, UV, repetição e custo de VRAM; o Asset Tool já implementa staging.
- Materiais tradicionais com **cor base, transparência e emissividade baseada na própria textura**, respeitando o formato legado.
- Usar os **valores escalares** de roughness e metallic já expostos pelo shader apenas em código, como no teste de esferas. Atribuir um perfil escalar por objeto via manifest externo **exige uma pequena integração no adapter/factory**, embora o HLSL já entenda os escalares.
- Criar mapas de normal/height no Blender ou Asset Tool para avaliação offline, sem efeito no jogo até sua ligação aos shaders.
- Comparação de shader de materiais já presente usando `Ctrl+F10` e esferas de calibração (se `CalibrationSpheres` estiver habilitado nas configurações).

## O que precisa ser desenvolvido no cliente MonoGame

1. **Sidecar opcional e versionado** por asset/material, ex. `materials/field/ricarten/village-2.materials.json`, contendo `material_index`, assinatura/sha256 da origem, textura base e paths externos; não alterar o SMD.
2. **Validação e binding** no `PristonMaterialRuntimeFactory` e `PristonShaderLibrary`: resolver mapas de forma segura, cache, fallback para materiais antigos, espaço de cor correto (base albedo = sRGB; normal/roughness/metallic/AO = dados lineares).
3. **Extensão dos recursos HLSL** `MaterialDecl.hlsli`/`Material.hlsli` e `Surface.fx`: sample normal tangente, metallic-roughness (ORM), emissive opcional, AO per material, depois height/parallax experimental. Cuidado com slots já ocupados por sombras, luzes e IBL; verificar limite documentado de 16 registros por estágio e alternativas de packing.
4. **Tangent/bitangent**: calcular no shader por derivadas de posição/UV ou adicionar dados de vértice em buffer/runtime; verificar winding, handedness e canal verde normal DX/OpenGL; oferecer fallback quando UV degenerada.
5. **QA GPU**: ampliar `ShaderValidationProbe*.cs` e ensaiar em cena real com diferentes horas, bloom, reflexos, alpha, TAA, SSAO. Não ativar globalmente antes de validar.
6. **Perfis/presets** por família (terrain/stone/wood/metal/skin etc.) e medição de throughput (FPS, GPU timestamps, memória/mipmaps, streaming) usando ferramentas do renderer.

Esta abordagem **não modifica o leitor de SMD/SMB/INX** nem importa os 11 packs Summit no jogo. Adiciona somente uma fonte auxiliar de parâmetros de materiais ao cliente, mantendo os arquivos nativos como autoridade da malha e UV.

## Sequência de experiências e gates

### Experimento A — substituição de textura em staging

- Usar textura real presente em `assets/Field/Ricarten` ou outra área sem falha de dependência (não usar a `tem_wall04.bmp` ausente).
- Executar inventário do script entregue e `texture_upscale.py capabilities` (verificar subcomando local com `--help` antes). Produzir **2×** com Lanczos como baseline controlado e depois Real-ESRGAN sob o mesmo conjunto de UVs.
- Verificar decode/encode/alpha, bordas tiling, hashes, diferença de dimensão, consumo estimado e visual no cliente de testes. **Não publicar por padrão**.
- Gate: carregamento no jogo, UV sem distorção, sem fallback, delta de FPS/VRAM medido e reversão.

### Experimento B — PBR no Blender

- Criar esfera ou plano com baseColor + normal + roughness + metallic + height + AO; exportar **GLB** e validar com `glTF Validator`/reimportação em Blender (passagem fora do jogo).
- Comprovar o que o GLB conserva. Confirmar que a exportação para SMD **não carrega automaticamente** os mapas PBR. Sem converter o GLB para jogo até implantar o sidecar.
- Gate: roundtrip do GLB e relatório de perda na conversão SMD.

### Experimento C — Metallic e Roughness **já existentes no renderer**

- Habilitar `CalibrationSpheres` em configuração de desenvolvimento; exibir esferas cinza e chrome; alternar `Ctrl+F10` pelas vistas Roughness, Metallic, AmbientOcclusion, Specular.
- Variar iluminação, câmaras e horas; rodar probes de shaders na máquina do usuário e documentar FPS/VRAM.
- Gate: **prova de renderer** com parâmetros escalares antes de alterar formato do material.

### Experimento D — Normal/Height

- Gerar normal DirectX e OpenGL da mesma referência conhecida; comparar no Blender. O shader do Etervale precisa de **alteração para renderizar normal map**; sem ela, o teste no jogo é esperado **não reagir** à troca do arquivo.
- Gate depois da integração: o debug-view Normals responde ao mapa técnico, sob múltiplos ângulos e com UV espelhados.

### Experimento E — PBR externo via manifesto

- Começar com **BaseColor + Metallic escalar + Roughness escalar**, depois `normalTexture` e mapa ORM. A instrumentação já suporta os parâmetros escalares.
- Extender readers de **materiais auxiliares**, sem alterar os readers SMD/SMB; comparar lado a lado com fallback antigo e criar testes C#.
- Gate: um objeto real em jogo com sidecar de roughness/metallic/normal, descarte/fallback sem crash e desempenho aceitável.

## Comando recomendado para obter inventário real na máquina do usuário

O script `etervale_graphics_audit_readonly.py` foi fornecido separadamente para a auditoria e **não integra esta branch do Etervale**. O comando seguinte pressupõe que o script foi guardado em `~/Transferências/`; não é uma funcionalidade instalada automaticamente com o jogo.

Instalar o script fornecido em `~/Transferências/` e executar:

```bash
cd "$HOME/Transferências/pt_asset_tool"
python3 "$HOME/Transferências/etervale_graphics_audit_readonly.py" \
  --game-root "$HOME/Transferências/Etervale-git" \
  --tool-root "$PWD" \
  --max-image-headers 500
```

O script emite JSON em `app/pt_build/etervale/graphics_audit/`, fora do Etervale, com contagens de extensões, tamanho total, amostra de dimensões e alpha, falhas em leitores de cabeçalho Pillow (**não são falhas confirmadas do reader do Asset Tool**), evidências estáticas de código e presença de executáveis opcionais. É apenas inventário; não testa compilação shaders nem fps.

## Evidência efetivamente executada neste ambiente

- Inspeção direta dos arquivos C#/HLSL/Python indicados.
- Smoke de `height_from_diffuse` e `normals_from_height` com imagem sintética 32×32: saída 32×32 RGB sem exceção.
- `python3 -m pytest -q app/dev_tests/test_texture_material_pipeline.py app/dev_tests/test_texture_upscale.py app/dev_tests/test_texture_material_profiles.py`: **50 testes aprovados**, sem acesso à GPU ou aos assets reais.
- Script de inventário executado contra a cópia do ZIP do jogo: retornou `asset_dir_exists=false` corretamente, sem inventar contagens de assets.

### O que não foi comprovado

- Montagem Vulkan e execução real dos shaders no computador do usuário; compilação/execução das ShaderValidationProbe nesta sessão.
- Resolução máxima efetiva de texturas e orçamento de VRAM; inventário por extensão e amostra de texturas protegidas.
- Ferramentas instaladas externamente e seus modelos/pesos (Blender, Real-ESRGAN, chaiNNer).
- Equivalência de materiais Blender↔jogo, múltiplos UVs no roundtrip, skeleton/animação complexa, qualidade visual e FPS.
- Liberação de um mapa PBR renderizado por um material de jogo: falta a extensão opcional de materials/sidecars.

## Decisão de engenharia

**APTO A PROTOTIPAR COM RESTRIÇÕES.** O Etervale já contém iluminação PBR substancial, e o Asset Tool já fornece processos offline relevantes. O ponto de integração ausente é o carregamento/binding dos **mapas e parâmetros por material** (sidecar + shader), não a BRDF nem o renderer inteiro. Iniciar por inventário de assets reais + esferas de calibração + upscale controlado; só então autorizar um patch opcional de materiais no cliente.

### Backend de upscale no ambiente de auditoria (não na máquina do usuário)

`python3 app/pt_texture_upscale.py capabilities` retornou `pillow_ready=true`, `lanczos_ready=true` e `realesrgan_ready=false` neste container, porque não há executável `realesrgan-ncnn-vulkan` configurado. Isso **não afirma** que o Real-ESRGAN esteja ausente na máquina do usuário. O comando deve ser repetido na instalação real para medir sua disponibilidade.
