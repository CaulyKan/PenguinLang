pipeline {
    agent {
        docker { image 'dotnet/sdk:8.0' }
    }

    environment {
        RELEASE_PATH = "\\\\192.168.0.101\\Workspace\\jenkins\\release\\${env.JOB_NAME}\\"
        TIMESTAMP = new Date().format('yyyyMMdd_HHmmss')
    }

    stages {
        stage('Checkout') {
            steps {
                // 从Git仓库拉取代码
                checkout scm
            }
        }
        
        stage('Build') {
            steps {
                sh "dotnet build"
            }
        }

        stage('Test') {
            steps {
                sh "dotnet test"
            }
        }
    }
    
}