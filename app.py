# app.py
import subprocess
import os
import http.client
import time
from flask import Flask, render_template, jsonify
from threading import Thread
from werkzeug.serving import run_simple


app = Flask(__name__)
server_process = None
server_status = "Неактивен"
output_file_path = "server_output.txt"
project_path = os.getcwd()

# Создаем файл 'output.txt' при запуске
with open(output_file_path, 'w') as output_file:
    output_file.write("")

def start_server():
    global server_status, server_process
    if server_status == "Неактивен":
        server_process = subprocess.Popen(
            ["dotnet", "run"],
            cwd=project_path,
            stdout=open(output_file_path, 'a'),  # 'a' означает режим добавления (append)
            stderr=subprocess.STDOUT,
        )
        server_status = "Активен"

def stop_server():
    global server_status, server_process
    if server_status == "Активен" and server_process is not None:
        server_process.terminate()
        server_status = "Неактивен"

def has_changes():
    # Проверяем, есть ли изменения после git pull
    subprocess.run(['git', 'fetch'], check=True)
    local_branch = subprocess.run(['git', 'rev-parse', 'HEAD'], stdout=subprocess.PIPE, text=True).stdout.strip()
    remote_branch = subprocess.run(['git', 'rev-parse', 'FETCH_HEAD'], stdout=subprocess.PIPE, text=True).stdout.strip()
    return local_branch != remote_branch

def pull_and_restart():

    # Проверяем изменения
    if has_changes():
        print("Changes detected. Restarting server...")
        # Выполняем git pull
        stop_server()

        # Откатываем все изменения (включая проиндексированные файлы)
        subprocess.run(['git', 'reset', '--hard'], check=True)
        subprocess.run(['git', 'clean', '-fd'], check=True)
        subprocess.run(['git', 'pull', '--force'], check=True)

        start_server()

def run_flask():
    run_simple("0.0.0.0", 5400, app, use_reloader=False)

@app.route('/')
def index():
    output_content = ""
    with open(output_file_path, 'r') as output_file:
        output_content += output_file.read()
    print(output_content)
    return render_template('index.html', server_status=server_status, output_content=output_content)

@app.route('/get_logs')
def get_logs():
    with open(output_file_path, 'r') as output_file:
        content = output_file.read()
        lines = content.split('\n')  # Split the content into lines
        last_40_lines = lines[-40:]
    return '\n'.join(last_40_lines)


@app.route('/turn_on')
def turn_on():
    global server_status
    with open(output_file_path, 'w') as output_file:
        output_file.write("")
    pull_and_restart()  # Вызываем функцию pull_and_restart перед стартом сервера
    start_server()
    return 'Включено'

@app.route('/turn_off')
def turn_off():
    global server_status
    stop_server()
    return 'Выключено'

@app.route('/get_status')
def get_status():
    return server_status

@app.errorhandler(500)
def internal_server_error(error):
    return f'Internal Server Error: {error}', 500

if __name__ == '__main__':
    print("Start Web-Server Program")

    # Запускаем Flask в отдельном потоке
    flask_thread = Thread(target=run_flask)
    flask_thread.start()

    # Добавляем бесконечный цикл для автоматического обновления каждые 5 минут
    while True:
        try:
            pull_and_restart()
            print("Pull and restart successful.")
        except subprocess.CalledProcessError as e:
            print(f"Error: {e}")

        time.sleep(15)  # 5 минут