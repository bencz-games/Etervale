# Meteo — impacto e temporização visual

Meteo do Mago (`0x08080004`) tem um efeito visual de queda (`vfx.magician.meteo.fall`), que inicia o efeito de choque no solo (`vfx.magician.meteo.hit`). No teste original do projeto extraído do ZIP, a queda começava quando o servidor já tinha resolvido o primeiro golpe. Uma correção local antecipou a animação cerca de 970 ms, e foi confirmada visualmente nesse cliente.

## Adaptação para a `main` atual

A sequência usada nesse teste era **66 fotogramas, evento 42 e golpe no fim**. A `main` do repositório passou a usar **117 fotogramas e dois eventos, 17 e 83**. A alteração aqui mantém **inalterados os dois eventos e os cálculos de dano do servidor**.

À velocidade de referência (33 ms por fotograma), os golpes são aproximadamente a **561 ms** e **2739 ms**. O primeiro é anterior à duração aproximada de 970 ms da queda, pelo que já não é possível fazer o meteorito aterrar antes dele apenas antecipando um VFX que só pode começar após o início do lançamento.

A apresentação desta versão faz então o seguinte:

- O primeiro impacto conserva o dano do servidor e mostra imediatamente `vfx.magician.meteo.hit`.
- A queda `vfx.magician.meteo.fall` inicia aproximadamente 970 ms **antes do segundo impacto**. O efeito completo de queda gera o seu próprio choque visual.
- O segundo impacto não cria uma segunda cópia da queda, nem reproduz o som de impacto outra vez.
- Um cancelamento do lançamento remove a queda ainda pendente.

`SkillRule.PreImpactHitIndex` escolhe o índice (a partir de zero) do evento autoral a sincronizar; `PreImpactLeadMs` define a antecedência. Para um segundo evento, o cliente utiliza a duração comunicada no evento `Start` e a proporção dos fotogramas do movimento. Isto respeita a velocidade real do lançamento sem mexer nas decisões do servidor.

## Verificação

1. Executar `dotnet run --project tests/PristonTale.Tests -c Release` com o runtime MonoGame compilado.
2. Com os recursos VFX em `assets/`, executar o jogo com Mago GM e `/unlocktier 4`.
3. Confirmar que o primeiro golpe apresenta um choque visual, que a queda chega ao solo perto do segundo golpe e que não aparece uma segunda queda atrasada.
4. Testar um lançamento interrompido e também a observação por um segundo cliente.

**Estado:** solução adaptada a partir do teste feito no ZIP. A sincronização visual desta nova sequência da `main` carece de confirmação dentro do jogo; os testes automatizados verificam a configuração, não substituem a observação da animação.
