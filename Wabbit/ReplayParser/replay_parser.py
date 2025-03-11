import json
import re

def parse_game_replay(file_path, output_path):
    with open(file_path, 'rb') as file:  # Open in binary mode
        # Read the first two lines of the file
        lines = [file.readline().strip(), file.readline().strip()]
        
        # Combine lines to ensure we capture the JSON data
        combined_line = b''.join(lines).decode('ISO-8859-1', errors='ignore')  # Decode using ISO-8859-1
        
        # Find the starting point of the JSON data
        json_start = combined_line.find('{"game":')  # Search for the string
        if json_start == -1:
            print("No valid JSON data found.")
            return
        
        # Extract the JSON data starting from the found index
        json_data = combined_line[json_start:]  # This is already a string
        
        # Stop processing at the first occurrence of "star"
        star_index = json_data.find('star')
        if star_index != -1:
            json_data = json_data[:star_index]  # Keep only the part before "star"
        
        # Clean the JSON string to ensure it is valid
        json_data = _clean_json_string(json_data)
        
        # Load the JSON data
        try:
            data = json.loads(json_data)
        except json.JSONDecodeError as e:
            print(f"Error decoding JSON: {e}")
            print(f"Extracted JSON data: {json_data}")  # Print the problematic JSON for debugging
            return
        
        # Format the data
        formatted_data = _format_data(data)
        
        # Write the formatted data to the output file
        with open(output_path, 'w', encoding='utf-8') as output_file:
            json.dump(formatted_data, output_file, indent=4)
        
        print(f"Formatted data written to {output_path}")

def _clean_json_string(json_string):
    # Remove excessive backslashes
    json_string = re.sub(r'\\+', r'\\', json_string)  # Normalize backslashes
    json_string = json_string.replace('\\"', '"')  # Replace escaped quotes with normal quotes
    
    # Remove any leading or trailing whitespace
    json_string = json_string.strip()
    
    return json_string

def _format_data(data):
    # Reorganize the data as needed
    formatted = {
        "game": {
            "GameMode": data.get("game", {}).get("GameMode"),
            "Map": data.get("game", {}).get("Map"),
            "Players": []
        }
    }
    
    # Extract player data
    for key in data.keys():
        if key.startswith("player_"):
            player_info = data[key]
            formatted["game"]["Players"].append({
                "PlayerUserId": player_info.get("PlayerUserId"),
                "PlayerName": player_info.get("PlayerName"),
                "PlayerElo": player_info.get("PlayerElo"),
                "PlayerRank": player_info.get("PlayerRank"),
                "PlayerLevel": player_info.get("PlayerLevel"),
                "PlayerAlliance": player_info.get("PlayerAlliance"),
                "PlayerScoreLimit": player_info.get("PlayerScoreLimit"),
                "PlayerIncomeRate": player_info.get("PlayerIncomeRate"),
                "PlayerAvatar": player_info.get("PlayerAvatar"),
                "PlayerReady": player_info.get("PlayerReady"),
            })
    
    return formatted

# Example usage
if __name__ == "__main__":
    input_file_path = 'C:/Users/coleg/Saved Games/EugenSystems/WARNO/11-29-NBK-game1.rpl3'  # Change this to your input file path
    output_file_path = 'C:/Users/coleg/Saved Games/EugenSystems/WARNO/11-29-NBK-game1.json'  # Change this to your desired output file path
    parse_game_replay(input_file_path, output_file_path)
