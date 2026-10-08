# Connecter un bot REST

## Identité

Le moteur appelle `POST {baseUrl}/name`, sans corps. Répondre avec un code 200 et un objet JSON :

```json
{ "name": "Mon bot", "email": "moi@example.com" }
```

Le nom doit contenir 1 à 40 caractères. L'email est compatible avec le protocole d'origine ; cette version ne le conserve pas. L'URL doit être HTTP(S), sans identifiants ni query string. Un préfixe de chemin est accepté.

## Décision

À chaque tour où le joueur est vivant, le moteur appelle `POST {baseUrl}/move` avec `Content-Type: application/json`. Voici un exemple de structure ; les coordonnées et identifiants changent à chaque partie :

```json
{
  "game": { "id": "d5c3b329-63f4-4710-a589-ab3010c04f69" },
  "player": {
    "id": "0981d954-6614-478b-93b6-0869e3dba15d",
    "name": "Mon bot",
    "position": { "x": 4, "y": 3 },
    "previous": { "x": 4, "y": 2 },
    "area": { "x1": 1, "y1": 0, "x2": 7, "y2": 6 },
    "fire": true
  },
  "board": {
    "size": { "width": 25, "height": 17 },
    "walls": [{ "x": 3, "y": 3 }]
  },
  "players": [{ "x": 6, "y": 3 }],
  "enemies": [{ "x": 4, "y": 5, "neutral": false }]
}
```

Répondre par exemple avec `{ "move": "fire-down" }`. Le moteur ne transmet ni le score ni le numéro du tour, pour préserver le protocole du PDF. On peut garder un état interne indexé par `game.id` et `player.id`, à condition de gérer les appels concurrents. `previous` est la position au début du dernier tour résolu, et peut être identique à `position`.

Les réponses doivent arriver en 3 secondes et rester sous 16 Ko. Les redirections ne sont pas suivies. Une erreur HTTP, une réponse JSON malformée ou une commande inconnue déclenche l'attente et un événement dans le journal. Tirer pendant la recharge n'a aucun effet. Une collision avec un mur empêche le déplacement.

## API de l'arène

| Méthode et chemin | Corps / rôle |
| --- | --- |
| `GET /health` | Vérifier le serveur |
| `GET /api/bots` | Lister les bots intégrés et connectés |
| `POST /api/bots` | `{ "url": "https://example.com/bot/" }` ; vérifie `/name` et persiste l'identité |
| `POST /api/matches` | `{ "options": { "seed": 2020, "maxTurns": 150 }, "botIds": ["hunter", "survivor"] }` ; crée une partie |
| `GET /api/matches/{id}` | Dernier état résolu, pour le spectateur |
| `POST /api/matches/{id}/step` | Sans corps ; interroge les bots et avance d'un tour |
| `DELETE /api/matches/{id}` | Libère la partie conservée en mémoire |

Les options omises prennent les valeurs par défaut. Les paramètres doivent respecter les bornes du moteur. Jusqu'à 9 bots différents par partie. La limite HTTP globale est de 600 demandes/minute/adresse, avec jusqu'à 8 opérations de simulation simultanées ; les appels à l'intérieur d'une partie restent parallèles, les étapes d'une même partie sont sérialisées.

## Replays

Le console exporte `{ "version": 1, "frames": [GameSnapshot, ...] }`. L'interface exporte une variante compacte `{ "version": 2, "initial": GameSnapshot, "frames": [...] }`, dont les frames omettent `walls` et `options`. Le lecteur web accepte les deux formats, jusqu'à 5001 frames et 32 Mo. Les replays n'embarquent aucun code exécutable.

## Développer en C# directement

Le moteur expose `IGameBot`, avec une identité et `ValueTask<BotDecision> DecideAsync(Observation, CancellationToken)`. Créer une instance par partie pour éviter de partager un état mutable entre simulations. Les bots hébergés dans le processus doivent coopérer avec l'annulation ; le moteur ne constitue pas un sandbox pour du code tiers. La version web utilise des bots HTTP externes et ne charge pas de code utilisateur sur le serveur.
