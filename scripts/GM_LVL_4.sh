# esse script recebe o nome da conta como parâmetro

cd ~/Transferências/Etervale-git || exit 1

dotnet run --project tools/PristonTale.DevAccounts \
  -c Release --no-launch-profile -- \
  set-gm "$1" 4 \
  --sqlite "$PWD/data/pristontale.db"
