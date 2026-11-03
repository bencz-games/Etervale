namespace PristonTale.Application.World;

internal sealed class MonsterDamageLog
{
    public ulong KillerId { get; private set; }

    public ulong FirstAttackerId { get; private set; }

    public int FirstAttackerDamage { get; private set; }

    public long FirstAttackerLastHitMs { get; private set; }

    public void RecordHit(ulong attackerId, int damage, bool monsterAtFullLifeBeforeHit, long nowMs)
    {
        if (attackerId == 0 || damage <= 0)
            return;

        KillerId = attackerId;

        if (FirstAttackerId == 0 && monsterAtFullLifeBeforeHit)
            FirstAttackerId = attackerId;

        if (attackerId == FirstAttackerId)
        {
            FirstAttackerDamage += damage;
            FirstAttackerLastHitMs = nowMs;
        }
    }
}
