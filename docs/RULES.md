# Règles et décisions de la version initiale

Source : le fichier PDF fourni avec la demande, pages 1 à 3 et 5 à 11. Le document original reste dans le dossier de travail et n'est pas recopié dans le dépôt. Le format de compétition et les modalités d'inscription historiques de la page 4 ne font pas partie du moteur demandé.

## Règles reprises

- Labyrinthe aléatoire de 10 à 50 colonnes et 10 à 25 lignes ; de 1 à 9 joueurs.
- Parties de 50 à 5000 tours. Tous les bots vivants sont interrogés en parallèle, avec un délai de 3 secondes. Une erreur ou un timeout donne une action nulle.
- Huit commandes : `up`, `down`, `left`, `right`, `fire-up`, `fire-down`, `fire-left`, `fire-right`.
- Les déplacements sont résolus avant les tirs, puis les ennemis se déplacent. Un joueur peut esquiver un tir ; un ennemi ne peut pas l'esquiver.
- Deux joueurs ou plus arrivant sur la même case meurent tous, y compris un joueur immobile visé par un déplacement.
- Les tirs réciproques peuvent tuer les deux joueurs. Les cibles sont calculées avant d'appliquer les morts.
- Un ennemi neutre meurt au contact ; un ennemi hostile tue le joueur. Les deux types peuvent être éliminés par un tir.
- Réapparition à la case de mort après un délai ; recharge du tir après un délai.
- Les bots reçoivent la taille totale mais uniquement les murs, joueurs vivants et ennemis dans leur zone visible. Coordonnées 0-based, origine en haut à gauche, limites du carré inclusives.
- Endpoints bots `POST /name` sans corps et `POST /move` avec le JSON du PDF. Le champ `fire` et la neutralité sont des booléens JSON, conformément à l'exemple complet, malgré les coquilles du schéma.

## Arbitrages explicités

Le PDF ne donne pas tous les paramètres numériques ni toutes les règles de résolution. Cette version fait les choix suivants :

| Sujet | Choix initial |
| --- | --- |
| Vision | Carré centré sur le joueur, rayon 3 par défaut, réduit aux bords de carte ; les murs n'occultent pas l'observation |
| Tir | Ligne droite, arrêt au premier mur ou à la première cible, portée égale aux limites de la vision |
| Munitions | Un tir disponible à la fois ; pas de stock fini pour toute la partie, car le PDF ne précise aucun stock ni réapprovisionnement |
| Recharge | 3 tours suivants interdits après un tir ; un tir au tour 1 permet de retirer au tour 5 |
| Réapparition | 5 tours complets d'attente ; mort au tour 1, retour au début du tour 7 ; le bot reste immobile au tour de retour et est interrogé au suivant |
| Case de retour occupée | Retour différé jusqu'à libération, pour conserver un seul occupant vivant par case |
| Échange de cases | Autorisé : les destinations sont différentes ; pas de collision sur les chemins |
| Spectres | 8 initiaux, un nouveau toutes les 8 étapes, maximum 30 ; 3 phases de mouvement neutres |
| Mouvement d'un spectre | Choix aléatoire uniforme parmi les cases cardinales libres et l'attente, sans traverser les murs ni superposer deux spectres ; ordre stable |
| Apparition d'un spectre | Case libre aléatoire, après la phase ennemis ; pas de contact immédiat à la naissance |
| Double tir sur une cible | Une seule récompense, attribuée au premier tireur dans l'ordre d'inscription ; tous les tirs restent visibles |
| Score | +1 par tour terminé vivant, +5 par spectre éliminé, +10 par bot éliminé ; aucune pénalité de mort |
| Classement | Score décroissant, puis éliminations décroissantes, puis morts croissantes ; égalités conservées |
| Action nulle | Extension pratique du contrat : `{ "move": null }` pour attendre |

Les observations sont prises avant toute mutation du tour. Les joueurs réapparaissant dans ce tour ne sont pas interrogés avant le tour suivant. Un joueur tué par une collision ou un contact avant la phase de tirs ne tire pas. Un bot annulé n'avance pas le tour du moteur ; les éventuels effets internes du bot lui-même ne peuvent pas être annulés.

Le PDF contient une incohérence de durée : 50 tours à 3 secondes maximum donnent **150 secondes (2,5 minutes)**, pas 2,5 secondes. Le simulateur ne rajoute aucun délai par défaut ; les bots intégrés répondent immédiatement. La vitesse de l'interface contrôle uniquement l'intervalle de lecture.

## Reproductibilité

Une même graine, la même configuration et les mêmes bots intégrés dans le même ordre donnent les mêmes murs, positions, ennemis et scores sous .NET 10. L'identifiant de partie reste unique. Les identifiants d'entités viennent du générateur avec graine. Cette garantie est limitée à cette version du moteur/runtime ; un bot HTTP peut être non déterministe. Un replay est la trace enregistrée des tours et se relit sans exécuter les stratégies.

Le labyrinthe est creusé par recherche en profondeur avec boucles supplémentaires ; toutes ses cases libres sont reliées. Le moteur protège chaque étape contre des demandes concurrentes et conserve un journal borné.
