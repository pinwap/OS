from PIL import Image
import numpy as np

def to_grayscale(image_path, output_path):
    """
    Converts an image to grayscale and saves the result.

    Parameters:
    - image_path: str, path to the input image.
    - output_path: str, path to save the grayscale image.
    """
    # Load image
    img = Image.open(image_path)
    
    # Convert to grayscale
    grayscale_img = img.convert('L')
    
    # Save the grayscale image
    grayscale_img.save(output_path)
    print(f"Grayscale image saved to {output_path}")

input_image_path = 'airport_1024.tiff'  # Replace with your input image path
output_image_path = 'grayscale_airport_1024.png'  # Replace with
to_grayscale(input_image_path, output_image_path)