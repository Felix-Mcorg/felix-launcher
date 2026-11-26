# Corrections (téléchargement des mods)

- FileDownloader : un délai dépassé (TaskCanceledException) était traité comme une annulation et faisait
  tout planter ; il déclenche maintenant une nouvelle tentative avec reprise (.part).
- FileDownloader : une connexion bloquée n'avait aucun délai (attente quasi infinie) ; abandon après 30 s sans donnée.
- FileDownloader : l'erreur finale indique enfin la vraie cause (404, 403, connexion bloquée...).
- FileDownloader : un .part inutilisable (416) sans empreinte n'est plus accepté tel quel.
- ManifestSyncService : un seul fichier en timeout n'annule plus tout le téléchargement.
- ManifestSyncService : progression limitée à ~10 mises à jour/s (l'interface était inondée).
- PlayService : « temps restant » basé sur une vitesse lissée (SpeedMeter) et masqué s'il est absurde.
- ByteSize.FormatDuration : gère les heures, masque au-delà de 24 h.
