# Hollow Arena

Une première version du challenge Halloween en **C# 14 / .NET 10 LTS** : moteur indépendant, simulateur console, arène web et bots REST. Les graphismes vectoriels sont originaux et dessinés dans Canvas, sans assets du PDF ni dépendance à un moteur JavaScript.

## Démarrer

Installer un SDK .NET 10 stable. `global.json` utilise le SDK 10.0.302 disponible lors du développement et accepte les feature bands stables ultérieures de .NET 10.

```powershell
dotnet build Halloween.slnx
dotnet test Halloween.slnx
dotnet run --project src/Halloween.Web
```

Ouvrir **http://localhost:5080**. Sélectionner des bots, créer une partie puis lancer la simulation ou avancer tour par tour. Trois bots intégrés permettent de commencer sans serveur supplémentaire. Le menu Vision affiche la carte entière ou le carré visible d'un bot. Le classement et le journal suivent chaque tour.

La vitesse règle la lecture ; un bot REST lent peut prendre jusqu'à 3 secondes par tour. Pause arrête la prochaine demande de tour ; un tour déjà demandé termine normalement. Le curseur relit les tours enregistrés. Exporter sauvegarde le replay, et Replay importe un fichier web ou console sans appeler les bots.

## Ajouter son bot

Dans un second terminal :

```powershell
dotnet run --project examples/Halloween.SampleBot
```

Dans **Mes bots**, connecter `http://localhost:5081`. Son nom vient de `POST /name`. Il est ensuite disponible dans la sélection de la prochaine partie. Le registre est conservé dans `src/Halloween.Web/App_Data/bots.json`.

Le bouton **Télécharger le bot C#** de l'interface fournit aussi un projet autonome : décompresser, lancer `dotnet run`, puis connecter `http://localhost:5081`. Pour régénérer l'archive après une modification du protocole ou de la stratégie, exécuter `powershell -File scripts/package-bot.ps1`.

Modifier `examples/Halloween.SampleBot/Program.cs` pour changer la stratégie. Tout langage convient : le serveur du bot doit exposer `POST /name` et `POST /move`. Le moteur envoie l'observation puis attend une réponse JSON, par exemple `{ "move": "fire-right" }`. Les URLs avec un préfixe, comme `https://example.com/my-bot/`, sont prises en charge. Le détail du protocole est dans [docs/BOTS.md](docs/BOTS.md).

L'exemple C# emploie `Random.Shared` : ses décisions ne sont pas reproductibles. Les trois bots intégrés, le labyrinthe et les ennemis utilisent des générateurs avec graine. Le registre ne stocke pas les emails.

## Simuler sans navigateur

```powershell
dotnet run --project src/Halloween.Simulator -- --seed 2020 --turns 150
dotnet run --project src/Halloween.Simulator -- --seed 42 --turns 500 --games 20 --output replay.json
```

Chaque partie utilise la graine précédente + 1. Le classement moyen est affiché ; `--output` exporte la dernière partie avec tous ses tours. Le fichier peut être importé dans l'arène. Le simulateur console utilise les bots intégrés ; les bots externes s'affrontent dans la version web.

## Architecture

| Projet | Rôle |
| --- | --- |
| `src/Halloween.Engine` | Labyrinthe, observations, résolution des tours, bots intégrés ; aucune dépendance ASP.NET |
| `src/Halloween.Simulator` | Simulations en série et export de replay en streaming |
| `src/Halloween.Web` | API ASP.NET Core, registre persistant, parties en mémoire, interface Canvas |
| `examples/Halloween.SampleBot` | Bot C# compatible prêt à modifier |
| `tests/Halloween.Tests` | Tests de règles, reproductibilité, invariants et protocole distant |

Les durées, la taille, les ennemis et la vision sont configurables. Les règles fidèles au PDF et les arbitrages nécessaires sont détaillés dans [docs/RULES.md](docs/RULES.md). Le PDF est une spécification de jeu : les instructions d'inscription et de compétition de 2020 ne sont pas exécutées.

## Version web et hébergement

L'application sert elle-même son frontend ; aucun Node.js n'est nécessaire pour jouer. Docker fournit une autre façon de la lancer :

```powershell
docker compose up --build
```

L'arène est alors sur http://localhost:5080, avec un volume pour le registre. En Production, seuls les bots à adresse Internet publique sont autorisés. Les redirections HTTP, adresses privées, link-local et réseaux réservés sont bloqués lors de la connexion réseau. `Bots:AllowLoopback=true` autorise uniquement le loopback pour le développement. Un bot local vu depuis Docker doit être accessible sur une adresse publique ou testé avec `dotnet run` hors du conteneur.

Cette version est un simulateur partagé pour prototyper : les parties sont en mémoire, jusqu'à 32 simultanément conservées, et le registre est partagé. Remplacer sa partie depuis le navigateur supprime la précédente. Un redémarrage efface les parties mais conserve les bots. Aucun système de comptes ni tournoi historique n'est fourni. Pour un service public ouvert, ajouter l'authentification, la propriété des bots/parties et une base de données, et placer le service derrière HTTPS. Le conteneur s'exécute sans privilèges ; le répertoire `App_Data` doit être accessible en écriture au compte applicatif. Le Dockerfile et Compose sont fournis mais nécessitent un environnement Docker pour validation.

## Vérification du navigateur

Après démarrage de l'arène et du bot exemple :

```powershell
npm install
npx playwright install chromium
npm run test:web
```

On peut utiliser un Edge installé avec `$env:BROWSER_CHANNEL='msedge'`. Les captures et replays de vérification sont écrits dans `artifacts/` (ignoré par Git). La vérification parcourt une partie complète, la connexion du bot, la vision partielle, la pause, les replays et une largeur mobile de 390 px.
