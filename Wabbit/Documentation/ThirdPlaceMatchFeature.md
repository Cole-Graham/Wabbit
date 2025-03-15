# Third Place Match Feature

## Overview

The Third Place Match feature allows tournament administrators to add a match to determine the third-place winner after both semifinals are completed. This feature is particularly useful when:

- The tournament initially did not include a third place match
- Tournament organizers decide to add a consolation match after seeing player/audience interest
- Additional ranking information is needed for prizes or seeding in future tournaments

## How It Works

### Automatic Notification

After both semifinal matches are completed, tournament administrators will see a button appear in the tournament status message:

```
[Create Third Place Match (Admin)] 🏅
```

This button is **only visible to users with the Manage Server permission**.

### Creating the Match

When an administrator clicks the button, the system will:

1. Create a third place match between the two semifinal losers
2. Use the same format (Best-of-X) as the semifinal matches
3. Update the tournament bracket visualization
4. Send a notification to the tournament channel

### Match Format

- The third place match uses the same Best-of-X format as semifinal matches
- If the tournament settings specify semifinal matches as Best-of-3, the third place match will also be Best-of-3
- The format ensures consistency in the playoff stage

## Important Notes

### Limitations

- The third place match can only be added after both semifinals are completed
- Once created, the third place match cannot be removed
- Only one third place match can be created per tournament

### Technical Details

- The match is automatically linked to both semifinal matches
- The semifinal losers are automatically advanced to the third place match
- The match appears in tournament visualizations and standings

## Troubleshooting

If the third place match button doesn't appear:
- Verify that both semifinal matches are fully completed
- Ensure you have the Manage Server permission
- Check if a third place match already exists

## Best Practices

- Communicate with participants before adding a third place match
- Consider whether the additional match adds value to the tournament
- Ensure both semifinal losers are available to play the additional match 