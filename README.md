## Maps.json structure and sample

*Created after application launch if not present*

| Key  | Description |
| ------------- | ------------- |
| Name  | Map display name  |
| Id  | Map ID  |
| Thumbnail  | Image URL. Will be shown in random map embed if specified  |
| Types  | Collection of values  |

**Types**

| Description  | Available values |
| ------------- | ------------- |
| Size  | "1v1", "2v2"  |
| Specifies if a map is included into random pool  | "Random"  |

```
[
  {
    "Name": "Punchbowl",
    "Id": "Conquete_3x3_Muju_Alt",
    "Thumbnail": "https://cdn.discordapp.com/attachments/1238497574122553394/1238513545142861914/Punchbowl.png?ex=663f8f1f&is=663e3d9f&hm=85b647d2c54aac3c46d47d49c4de4557167a68e86af3477c5d78cb361c293b1b&",
    "Types": [
      "1v1",
      "Random"
    ]
  },
  {
    "Name": "Paddy Field",
    "Id": "Conquete_2x3_Tohoku_Alt",
    "Thumbnail": "https://cdn.discordapp.com/attachments/1238497574122553394/1238513545717485648/1vs1-PaddyField.png?ex=663f8f1f&is=663e3d9f&hm=9f83ac34dfdcca42915635218e05bc43d7dbea93fe22f02a732a0d372dad95b8&",
    "Types": [
      "1v1",
      "Random"
    ]
  }
]
